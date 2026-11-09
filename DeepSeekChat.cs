using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace DeepSeekChatApp
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ChatForm());
        }
    }

    internal sealed class ChatForm : Form
    {
        private const string ApiUrl = "https://api.deepseek.com/chat/completions";

        // 固定使用思考模式，强度固定为 high。
        private const string Model = "deepseek-flash";
        private const string ReasoningEffort = "high";

        private readonly TextBox _apiKeyBox;
        private readonly TextBox _inputBox;
        private readonly Button _sendButton;
        private readonly TextBox _replyBox;
        private readonly Label _statusLabel;

        private readonly Timer _thinkingTimer;
        private readonly Timer _typingTimer;
        private readonly StringBuilder _pending = new StringBuilder();
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();

        private int _thinkingFrame;
        private bool _busy;

        public ChatForm()
        {
            Text = "DeepSeek 对话";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(680, 620);
            MinimumSize = new Size(540, 480);
            Font = new Font("Microsoft YaHei", 9F);

            var keyLabel = new Label
            {
                Text = "API Key",
                Location = new Point(12, 14),
                AutoSize = true
            };

            _apiKeyBox = new TextBox
            {
                Location = new Point(80, 10),
                Width = ClientSize.Width - 92,
                UseSystemPasswordChar = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            _inputBox = new TextBox
            {
                Location = new Point(12, 42),
                Width = ClientSize.Width - 24,
                Height = 110,
                Multiline = true,
                AcceptsReturn = true,
                ScrollBars = ScrollBars.Vertical,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            _sendButton = new Button
            {
                Text = "发送",
                Location = new Point(12, 160),
                Size = new Size(90, 32)
            };
            _sendButton.Click += OnSendClick;

            _statusLabel = new Label
            {
                Location = new Point(112, 168),
                AutoSize = true,
                ForeColor = Color.DimGray
            };

            _replyBox = new TextBox
            {
                Location = new Point(12, 200),
                Width = ClientSize.Width - 24,
                Height = ClientSize.Height - 212,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                BackColor = Color.White,
                Font = new Font("Microsoft YaHei", 10F),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            Controls.AddRange(new Control[] { keyLabel, _apiKeyBox, _inputBox, _sendButton, _statusLabel, _replyBox });

            _thinkingTimer = new Timer { Interval = 100 };
            _thinkingTimer.Tick += delegate
            {
                _statusLabel.Text = "思考中 " + "|/-\\"[_thinkingFrame++ % 4];
            };

            _typingTimer = new Timer { Interval = 20 };
            _typingTimer.Tick += OnTypingTick;
        }

        private async void OnSendClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }

            string apiKey = _apiKeyBox.Text.Trim();
            string userText = _inputBox.Text.Trim();

            if (apiKey.Length == 0)
            {
                MessageBox.Show(this, "请先填写 API Key。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                _apiKeyBox.Focus();
                return;
            }

            if (userText.Length == 0)
            {
                MessageBox.Show(this, "请输入要发送的内容。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                _inputBox.Focus();
                return;
            }

            _busy = true;
            _sendButton.Enabled = false;
            _replyBox.Clear();
            _pending.Clear();
            _thinkingFrame = 0;
            _statusLabel.Text = "思考中 |";
            _thinkingTimer.Start();

            string error = null;
            try
            {
                await StreamChatAsync(apiKey, userText);
            }
            catch (Exception ex)
            {
                error = DescribeError(ex);
            }

            // 等待打字机把缓冲区剩余字符显示完。
            while (_pending.Length > 0)
            {
                await Task.Delay(30);
            }

            _typingTimer.Stop();
            _thinkingTimer.Stop();
            _statusLabel.Text = string.Empty;
            _sendButton.Enabled = true;
            _busy = false;

            if (error != null)
            {
                MessageBox.Show(this, error, "请求失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async Task StreamChatAsync(string apiKey, string userText)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            ServicePointManager.Expect100Continue = false;

            var request = (HttpWebRequest)WebRequest.Create(ApiUrl);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "text/event-stream";
            request.Headers[HttpRequestHeader.Authorization] = "Bearer " + apiKey;
            request.UserAgent = "DeepSeekChatWinForms/1.0";
            request.Timeout = 30000;
            request.ReadWriteTimeout = 300000;
            request.AllowReadStreamBuffering = false;

            var payload = new Dictionary<string, object>
            {
                { "model", Model },
                { "stream", true },
                { "reasoning_effort", ReasoningEffort },
                {
                    "messages", new object[]
                    {
                        new Dictionary<string, object>
                        {
                            { "role", "user" },
                            { "content", userText }
                        }
                    }
                }
            };

            byte[] body = Encoding.UTF8.GetBytes(_serializer.Serialize(payload));
            request.ContentLength = body.Length;

            using (Stream requestStream = await request.GetRequestStreamAsync())
            {
                await requestStream.WriteAsync(body, 0, body.Length);
            }

            using (var response = (HttpWebResponse)await request.GetResponseAsync())
            using (Stream stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (!line.StartsWith("data:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string data = line.Substring(5).Trim();
                    if (data.Length == 0)
                    {
                        continue;
                    }

                    if (data == "[DONE]")
                    {
                        break;
                    }

                    string content = ExtractDeltaContent(data);
                    if (content.Length > 0)
                    {
                        AppendContent(content);
                    }
                }
            }
        }

        // 解析流式分片，只取 delta.content；思考内容（reasoning_content）暂时丢弃。
        private string ExtractDeltaContent(string json)
        {
            var root = _serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null)
            {
                return string.Empty;
            }

            var choices = GetValue(root, "choices") as object[];
            if (choices == null || choices.Length == 0)
            {
                return string.Empty;
            }

            var choice = choices[0] as Dictionary<string, object>;
            if (choice == null)
            {
                return string.Empty;
            }

            var delta = GetValue(choice, "delta") as Dictionary<string, object>;
            if (delta == null)
            {
                return string.Empty;
            }

            object content = GetValue(delta, "content");
            return content == null ? string.Empty : Convert.ToString(content);
        }

        private static object GetValue(Dictionary<string, object> dict, string key)
        {
            object value;
            return dict.TryGetValue(key, out value) ? value : null;
        }

        private void AppendContent(string text)
        {
            if (_thinkingTimer.Enabled)
            {
                _thinkingTimer.Stop();
                _statusLabel.Text = string.Empty;
            }

            _pending.Append(text.Replace("\r\n", "\n").Replace('\r', '\n'));

            if (!_typingTimer.Enabled)
            {
                _typingTimer.Start();
            }
        }

        private void OnTypingTick(object sender, EventArgs e)
        {
            if (_pending.Length == 0)
            {
                return;
            }

            int count = Math.Min(3, _pending.Length);
            string chunk = _pending.ToString(0, count).Replace("\n", "\r\n");
            _pending.Remove(0, count);

            _replyBox.AppendText(chunk);
            _replyBox.SelectionStart = _replyBox.TextLength;
            _replyBox.SelectionLength = 0;
            _replyBox.ScrollToCaret();
        }

        private static string DescribeError(Exception ex)
        {
            var webException = ex as WebException;
            if (webException != null)
            {
                var response = webException.Response as HttpWebResponse;
                if (response != null)
                {
                    using (response)
                    using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    {
                        string body = reader.ReadToEnd();
                        if (body.Length > 500)
                        {
                            body = body.Substring(0, 500);
                        }
                        return "HTTP " + (int)response.StatusCode + " " + response.StatusDescription
                            + Environment.NewLine + body;
                    }
                }
                return webException.Message;
            }
            return ex.Message;
        }
    }
}
