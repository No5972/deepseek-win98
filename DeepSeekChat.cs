using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
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

    internal sealed class ChatMessage
    {
        public string Id;
        public string Role;              // user / assistant
        public string Content;
        public string ReasoningContent;  // 仅 assistant 有
        public DateTime CreatedAt;
    }

    internal sealed class ChatSession
    {
        public string Id;
        public string Title;
        public DateTime CreatedAt;
        public DateTime UpdatedAt;
        public string FilePath;
        public readonly List<ChatMessage> Messages = new List<ChatMessage>();

        public override string ToString()
        {
            return Title;
        }
    }

    internal sealed class ChatForm : Form
    {
        private const string ApiUrl = "https://api.deepseek.com/chat/completions";

        // 固定使用思考模式，强度固定为 high。
        private const string Model = "deepseek-flash";
        private const string ReasoningEffort = "high";

        private const string DefaultTitle = "新会话";
        private static readonly string SessionsDir =
            Path.Combine(Application.StartupPath, "sessions");

        // 图标以 base64 内嵌在源码里，运行时动态解码，不依赖外部文件。
        private const string AppIconBase64 = "AAABAAIAICAQAAAAAADoAgAAJgAAABAQEAAAAAAAKAEAAA4DAAAoAAAAIAAAAEAAAAABAAQAAAAAAIACAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgAAAgAAAAICAAIAAAACAAIAAgIAAAMDAwACAgIAAAAD/AAD/AAAA//8A/wAAAP8A/wD//wAA////AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/wAAAAAAAAAAAAAAAAAAD/8AAAAAAAAAAAAAAAAAAA/wAAAAAAAAAAAAAAAAAAD/8AAAAAAAAAAAAAAAAAAA/wAAAAAAAAAAAAAAAAAAAP8AAAAAAAAAAAAAAAAAAADwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA////////////wAH//+AD///gA///8Af///AH///wB///+A////gP///8H////B////AP///gB///wAP//4AB//+AgP//AMB//wHAf/4B4H/+A+A//g/wP/4f+D/+f/w////+P/////////////////////////////////////8oAAAAEAAAACAAAAABAAQAAAAAAMAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgAAAgAAAAICAAIAAAACAAIAAgIAAAMDAwACAgIAAAAD/AAD/AAAA//8A/wAAAP8A/wD//wAA////AAAAAAAAAAAAAAAAAAAAAAAAAAAADwAAAAAAAAD/AAAAAAAAAPAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA//8AAPgfAAD4HwAA/D8AAPw/AAD+fwAA/D8AAPgfAADwDwAA8Y8AAOGHAADnxwAA/+cAAP//AAD//wAA//8AAA==";
        private const string NewIconBase64 = "AAABAAEAEBAQAAAAAAAoAQAAFgAAACgAAAAQAAAAIAAAAAEABAAAAAAAwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACAAACAAAAAgIAAgAAAAIAAgACAgAAAwMDAAICAgAAAAP8AAP8AAAD//wD/AAAA/wD/AP//AAD///8AAAAAAAAAAAAAAAAAAAAAAAAA//////8AAAD//////wAAAPAAAAD/AAAA//////8AAADwAAAA/wAAAP//////AAAI8AAAAP8AgAi//////wAA+P+AD///AIiPu///AAAAC4v4iI8P8AAIuLi//w8AAIsI8Iv/AAAAsAiwCAAAAAD//wAA4AEAAOABAADgAQAA4AEAAOABAADgAQAA4AEAAOABAABgAQAAwAEAAAABAACAAwAAgAcAACQPAABmHwAA";
        private const string DeleteIconBase64 = "AAABAAEAEBAQAAAAAAAoAQAAFgAAACgAAAAQAAAAIAAAAAEABAAAAAAAwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACAAACAAAAAgIAAgAAAAIAAgACAgAAAwMDAAICAgAAAAP8AAP8AAAD//wD/AAAA/wD/AP//AAD///8AAAAAAAAAAAAAAAAAAAAB8AAB8AAAAAAAABEfAAAAHwAAER8AAAHwAAABEfAAEfAAAAARHwEfAAAAAAEREfAAAAAAABEfAAAAAAABERHwAAAAABEfAfAAAAAREfAAEfAAAREfAAABHwABHwAAAAAR8AAAAAAAAAAAAAAAAAAAAAD//wAA//kAAOf/AADD8wAAw+cAAOHHAADwjwAA+B8AAPw/AAD4HwAA8J8AAMHHAACD4wAAj/EAAP//AAD//wAA";

        private readonly SplitContainer _split;
        private readonly ListBox _sessionList;
        private readonly Button _newButton;
        private readonly Button _deleteButton;
        private readonly Button _toggleButton;
        private readonly ContextMenuStrip _sessionMenu;
        private readonly ToolTip _toolTip = new ToolTip();

        private readonly TextBox _apiKeyBox;
        private readonly TextBox _inputBox;
        private readonly Button _sendButton;
        private readonly TextBox _replyBox;
        private readonly Label _statusLabel;
        private readonly TextBox _thinkingBox;

        private readonly Timer _thinkingTimer;
        private readonly Timer _typingTimer;
        private readonly StringBuilder _pending = new StringBuilder();
        private readonly StringBuilder _reasoning = new StringBuilder();
        private readonly StringBuilder _lastReply = new StringBuilder();
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();

        private readonly List<ChatSession> _sessions = new List<ChatSession>();
        private ChatSession _current;
        private int _thinkingFrame;
        private bool _busy;

        public ChatForm()
        {
            Text = "DeepSeek 对话";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(900, 640);
            MinimumSize = new Size(700, 500);
            Font = new Font("Simsun", 9F);

            Icon appIcon = LoadIcon(AppIconBase64);
            if (appIcon != null)
            {
                Icon = appIcon;
            }

            KeyPreview = true;
            KeyDown += OnFormKeyDown;

            // ---------- 左侧：会话列表 ----------
            _split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterWidth = 5,
                Panel1MinSize = 120,
                FixedPanel = FixedPanel.Panel1
            };

            var toolbar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36
            };

            _newButton = new Button
            {
                Location = new Point(6, 6),
                Size = new Size(24, 24),
                FlatStyle = FlatStyle.Standard,
                Image = LoadIconImage(NewIconBase64)
            };
            _newButton.Click += OnNewSessionClick;
            _toolTip.SetToolTip(_newButton, "新建会话 (Ctrl+N)");

            _deleteButton = new Button
            {
                Location = new Point(36, 6),
                Size = new Size(24, 24),
                FlatStyle = FlatStyle.Standard,
                Image = LoadIconImage(DeleteIconBase64)
            };
            _deleteButton.Click += OnDeleteSessionClick;
            _toolTip.SetToolTip(_deleteButton, "删除会话 (Delete)");

            toolbar.Controls.Add(_newButton);
            toolbar.Controls.Add(_deleteButton);

            _sessionList = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.Fixed3D,
                IntegralHeight = false
            };
            _sessionList.SelectedIndexChanged += OnSessionSelected;
            _sessionList.KeyDown += OnSessionListKeyDown;

            _sessionMenu = new ContextMenuStrip();
            _sessionMenu.Items.Add("重命名", null, delegate { RenameSelectedSession(); });
            _sessionMenu.Items.Add("删除", null, delegate { DeleteSelectedSession(); });
            _sessionList.ContextMenuStrip = _sessionMenu;

            _split.Panel1.Controls.Add(_sessionList);
            _split.Panel1.Controls.Add(toolbar);

            // ---------- 右侧：对话区 ----------
            _toggleButton = new Button
            {
                Text = "＜",
                Location = new Point(8, 10),
                Size = new Size(26, 23),
                FlatStyle = FlatStyle.System
            };
            _toggleButton.Click += delegate
            {
                _split.Panel1Collapsed = !_split.Panel1Collapsed;
                _toggleButton.Text = _split.Panel1Collapsed ? "＞" : "＜";
            };
            _toolTip.SetToolTip(_toggleButton, "收起 / 展开会话列表");

            var keyLabel = new Label
            {
                Text = "API Key",
                Location = new Point(44, 14),
                AutoSize = true
            };

            _apiKeyBox = new TextBox
            {
                Location = new Point(112, 10),
                Width = 560,
                UseSystemPasswordChar = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            _inputBox = new TextBox
            {
                Location = new Point(12, 42),
                Width = 660,
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
                Size = new Size(90, 32),
                FlatStyle = FlatStyle.System
            };
            _sendButton.Click += OnSendClick;

            _statusLabel = new Label
            {
                Location = new Point(112, 168),
                AutoSize = true,
                ForeColor = Color.DimGray
            };

            // 单行、右对齐，只露出最新生成的思考内容。
            _thinkingBox = new TextBox
            {
                Location = new Point(190, 163),
                Width = 570,
                Height = 23,
                ReadOnly = true,
                TabStop = false,
                BorderStyle = BorderStyle.Fixed3D,
                TextAlign = HorizontalAlignment.Right,
                BackColor = SystemColors.Control,
                Font = new Font("Simsun", 9F),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            _replyBox = new TextBox
            {
                Location = new Point(12, 200),
                Width = 660,
                Height = 420,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                BackColor = SystemColors.Control,
                Font = new Font("Simsun", 9F),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            _split.Panel2.Controls.AddRange(new Control[]
            {
                _toggleButton, keyLabel, _apiKeyBox, _inputBox,
                _sendButton, _statusLabel, _thinkingBox, _replyBox
            });

            foreach (Control control in new Control[] { _apiKeyBox, _inputBox, _thinkingBox, _replyBox })
            {
                control.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            }
            _split.Panel2.SizeChanged += delegate { LayoutChatPanel(); };

            Controls.Add(_split);
            _split.SplitterDistance = 210;
            LayoutChatPanel();

            _thinkingTimer = new Timer { Interval = 100 };
            _thinkingTimer.Tick += delegate
            {
                _statusLabel.Text = "思考中 " + "|/-\\"[_thinkingFrame++ % 4];
            };

            _typingTimer = new Timer { Interval = 20 };
            _typingTimer.Tick += OnTypingTick;

            LoadSessions();
        }

        // ==================== 会话存储 ====================

        private void LoadSessions()
        {
            try
            {
                if (!Directory.Exists(SessionsDir))
                {
                    Directory.CreateDirectory(SessionsDir);
                }

                foreach (string path in Directory.GetFiles(SessionsDir, "*.json"))
                {
                    ChatSession session = ReadSessionFile(path);
                    if (session != null)
                    {
                        _sessions.Add(session);
                    }
                }
            }
            catch (Exception)
            {
                // 目录不可用时仍然可以当作临时会话使用。
            }

            _sessions.Sort(delegate(ChatSession a, ChatSession b)
            {
                return b.UpdatedAt.CompareTo(a.UpdatedAt);
            });

            if (_sessions.Count == 0)
            {
                _sessions.Add(CreateSession());
            }

            RefreshSessionList();
            _sessionList.SelectedIndex = 0;
        }

        private ChatSession CreateSession()
        {
            var session = new ChatSession
            {
                Id = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                    + "-" + Guid.NewGuid().ToString("N").Substring(0, 6),
                Title = DefaultTitle,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            session.FilePath = Path.Combine(SessionsDir, session.Id + ".json");
            SaveSession(session);
            return session;
        }

        private static ChatSession ReadSessionFile(string path)
        {
            try
            {
                var serializer = new JavaScriptSerializer();
                var root = serializer.DeserializeObject(File.ReadAllText(path, Encoding.UTF8))
                    as Dictionary<string, object>;
                if (root == null)
                {
                    return null;
                }

                var session = new ChatSession
                {
                    Id = AsString(GetValue(root, "id")),
                    Title = AsString(GetValue(root, "title")),
                    CreatedAt = ParseTime(GetValue(root, "created_at")),
                    UpdatedAt = ParseTime(GetValue(root, "updated_at")),
                    FilePath = path
                };

                if (session.Title.Length == 0)
                {
                    session.Title = DefaultTitle;
                }

                var messages = GetValue(root, "messages") as object[];
                if (messages != null)
                {
                    foreach (object item in messages)
                    {
                        var dict = item as Dictionary<string, object>;
                        if (dict == null)
                        {
                            continue;
                        }
                        session.Messages.Add(new ChatMessage
                        {
                            Id = AsString(GetValue(dict, "id")),
                            Role = AsString(GetValue(dict, "role")),
                            Content = AsString(GetValue(dict, "content")),
                            ReasoningContent = AsString(GetValue(dict, "reasoning_content")),
                            CreatedAt = ParseTime(GetValue(dict, "created_at"))
                        });
                    }
                }

                return session;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void SaveSession(ChatSession session)
        {
            try
            {
                if (!Directory.Exists(SessionsDir))
                {
                    Directory.CreateDirectory(SessionsDir);
                }

                var messages = new object[session.Messages.Count];
                for (int i = 0; i < session.Messages.Count; i++)
                {
                    ChatMessage message = session.Messages[i];
                    var dict = new Dictionary<string, object>
                    {
                        { "id", message.Id },
                        { "object", "chat.message" },
                        { "role", message.Role },
                        { "content", message.Content },
                        { "created_at", FormatTime(message.CreatedAt) }
                    };
                    if (!string.IsNullOrEmpty(message.ReasoningContent))
                    {
                        dict["reasoning_content"] = message.ReasoningContent;
                    }
                    messages[i] = dict;
                }

                var payload = new Dictionary<string, object>
                {
                    { "id", session.Id },
                    { "object", "chat.session" },
                    { "title", session.Title },
                    { "created_at", FormatTime(session.CreatedAt) },
                    { "updated_at", FormatTime(session.UpdatedAt) },
                    { "model", Model },
                    { "messages", messages }
                };

                File.WriteAllText(session.FilePath,
                    _serializer.Serialize(payload), new UTF8Encoding(false));
            }
            catch (Exception)
            {
                // 保存失败不打断对话。
            }
        }

        private static string FormatTime(DateTime time)
        {
            return time.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static DateTime ParseTime(object value)
        {
            DateTime parsed;
            string text = AsString(value);
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out parsed))
            {
                return parsed;
            }
            return DateTime.Now;
        }

        // ==================== 会话列表操作 ====================

        private void RefreshSessionList()
        {
            _sessionList.BeginUpdate();
            _sessionList.Items.Clear();
            foreach (ChatSession session in _sessions)
            {
                _sessionList.Items.Add(session);
            }
            _sessionList.EndUpdate();

            int index = _current == null ? -1 : _sessions.IndexOf(_current);
            if (index >= 0)
            {
                _sessionList.SelectedIndex = index;
            }
        }

        private void OnSessionSelected(object sender, EventArgs e)
        {
            var session = _sessionList.SelectedItem as ChatSession;
            if (session == null || session == _current || _busy)
            {
                return;
            }

            LoadSessionContent(session);
        }

        private void LoadSessionContent(ChatSession session)
        {
            _current = session;
            _reasoning.Clear();
            _thinkingBox.Clear();
            _statusLabel.Text = string.Empty;
            _replyBox.Text = BuildTranscript(session);
            MoveReplyCaretToEnd();
        }

        private void OnSessionListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F2)
            {
                RenameSelectedSession();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Delete)
            {
                DeleteSelectedSession();
                e.Handled = true;
            }
        }

        private void OnFormKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.N)
            {
                OnNewSessionClick(null, EventArgs.Empty);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private static Icon LoadIcon(string base64)
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(base64);
                using (var stream = new MemoryStream(bytes))
                {
                    return new Icon(stream);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Image LoadIconImage(string base64)
        {
            Icon icon = LoadIcon(base64);
            if (icon == null)
            {
                return null;
            }

            using (icon)
            using (Bitmap source = icon.ToBitmap())
            {
                return new Bitmap(source, new Size(16, 16));
            }
        }

        private void OnNewSessionClick(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }

            var session = CreateSession();
            _sessions.Insert(0, session);
            _current = session;
            RefreshSessionList();
            _sessionList.SelectedIndex = 0;
            _replyBox.Clear();
            _thinkingBox.Clear();
            _inputBox.Clear();
            _inputBox.Focus();
        }

        private void OnDeleteSessionClick(object sender, EventArgs e)
        {
            DeleteSelectedSession();
        }

        private void DeleteSelectedSession()
        {
            if (_busy)
            {
                return;
            }

            var session = _sessionList.SelectedItem as ChatSession;
            if (session == null)
            {
                return;
            }

            if (MessageBox.Show(this, "确定删除会话“" + session.Title + "”？", "删除会话",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            {
                return;
            }

            try
            {
                if (session.FilePath != null && File.Exists(session.FilePath))
                {
                    File.Delete(session.FilePath);
                }
            }
            catch (Exception)
            {
                // 文件删不掉时只从列表移除。
            }

            int index = _sessions.IndexOf(session);
            _sessions.Remove(session);
            if (_current == session)
            {
                _current = null;
            }

            if (_sessions.Count == 0)
            {
                _sessions.Add(CreateSession());
            }

            RefreshSessionList();
            int target = Math.Min(Math.Max(index, 0), _sessions.Count - 1);
            _sessionList.SelectedIndex = target;
            // 自动选中的会话可能和原来索引相同而不触发选中事件，这里显式加载内容。
            LoadSessionContent(_sessions[target]);
        }

        private void RenameSelectedSession()
        {
            if (_busy)
            {
                return;
            }

            var session = _sessionList.SelectedItem as ChatSession;
            if (session == null)
            {
                return;
            }

            string name = Microsoft.VisualBasic.Interaction.InputBox(
                "输入新的会话标题：", "重命名会话", session.Title);
            name = name.Trim();
            if (name.Length == 0 || name == session.Title)
            {
                return;
            }

            session.Title = name;
            session.UpdatedAt = DateTime.Now;
            SaveSession(session);
            RefreshListText();
        }

        private void LayoutChatPanel()
        {
            int width = _split.Panel2.ClientSize.Width;
            int height = _split.Panel2.ClientSize.Height;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            _apiKeyBox.Width = Math.Max(80, width - 124);
            _inputBox.Width = Math.Max(120, width - 24);
            _thinkingBox.Width = Math.Max(60, width - 202);
            _replyBox.Width = Math.Max(120, width - 24);
            _replyBox.Height = Math.Max(80, height - 212);
        }

        private void RefreshListText()
        {
            int index = _sessionList.SelectedIndex;
            _sessionList.BeginUpdate();
            _sessionList.Items.Clear();
            foreach (ChatSession session in _sessions)
            {
                _sessionList.Items.Add(session);
            }
            _sessionList.EndUpdate();

            if (index >= 0 && index < _sessionList.Items.Count)
            {
                _sessionList.SelectedIndex = index;
            }
        }

        // ==================== 发送 ====================

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

            if (_current == null)
            {
                _current = CreateSession();
                _sessions.Insert(0, _current);
                RefreshSessionList();
                _sessionList.SelectedIndex = 0;
            }

            var userMessage = new ChatMessage
            {
                Id = Guid.NewGuid().ToString("N"),
                Role = "user",
                Content = userText,
                CreatedAt = DateTime.Now
            };
            _current.Messages.Add(userMessage);
            _current.UpdatedAt = DateTime.Now;
            SaveSession(_current);

            DateTime responseStartedAt = DateTime.Now;
            _replyBox.AppendText((_replyBox.TextLength > 0 ? "\r\n\r\n" : string.Empty)
                + FormatSpeaker("user", userMessage.CreatedAt) + "\r\n"
                + userText.Replace("\n", "\r\n") + "\r\n\r\n"
                + FormatSpeaker("assistant", responseStartedAt) + "\r\n");
            MoveReplyCaretToEnd();
            _inputBox.Clear();

            SetBusy(true);
            _pending.Clear();
            _reasoning.Clear();
            _lastReply.Clear();
            _thinkingBox.Clear();
            _thinkingFrame = 0;
            _statusLabel.Text = "思考中 |";
            _thinkingTimer.Start();

            string error = null;
            try
            {
                await StreamChatAsync(apiKey, _current);
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
            SetBusy(false);

            if (error != null)
            {
                MessageBox.Show(this, error, "请求失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var assistant = new ChatMessage
            {
                Id = Guid.NewGuid().ToString("N"),
                Role = "assistant",
                Content = _lastReply.ToString(),
                ReasoningContent = _reasoning.ToString(),
                CreatedAt = responseStartedAt
            };
            _lastReply.Clear();

            _current.Messages.Add(assistant);
            _current.UpdatedAt = DateTime.Now;
            SaveSession(_current);
            RefreshListText();

            // 服务端没给标题时按文档建议兜底：取用户消息的前若干个字符。
            if (_current.Title == DefaultTitle)
            {
                string fallback = FirstUserContent(_current);
                if (fallback.Length > 12)
                {
                    fallback = fallback.Substring(0, 12);
                }
                if (fallback.Length > 0)
                {
                    ApplyTitle(_current, fallback);
                }
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _sendButton.Enabled = !busy;
            _sessionList.Enabled = !busy;
            _newButton.Enabled = !busy;
            _deleteButton.Enabled = !busy;
        }

        private async Task StreamChatAsync(string apiKey, ChatSession session)
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
                { "messages", BuildRequestMessages(session) }
            };

            // 仅在首轮对话开启标题生成，标题随回答一起返回，不额外计费。
            if (IsFirstRound(session))
            {
                payload["title"] = new Dictionary<string, object> { { "enabled", true } };
            }

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

                    var delta = ExtractDelta(data);
                    if (delta == null)
                    {
                        continue;
                    }

                    string reasoning = ReadString(delta, "reasoning_content");
                    if (reasoning.Length > 0)
                    {
                        AppendReasoning(reasoning);
                    }

                    // 标题随流式分片返回（最后一个 chunk 的 delta.title）。
                    string title = ReadString(delta, "title").Trim();
                    if (title.Length > 0 && _current == session)
                    {
                        ApplyTitle(session, title);
                    }

                    string content = ReadString(delta, "content");
                    if (content.Length > 0)
                    {
                        AppendContent(content);
                    }
                }
            }
        }

        // 历史多轮：只回传 role/content，思考内容不入参（与 OpenAI 兼容格式一致）。
        private static object[] BuildRequestMessages(ChatSession session)
        {
            var messages = new object[session.Messages.Count];
            for (int i = 0; i < session.Messages.Count; i++)
            {
                ChatMessage message = session.Messages[i];
                messages[i] = new Dictionary<string, object>
                {
                    { "role", message.Role },
                    { "content", message.Content }
                };
            }
            return messages;
        }

        private static bool IsFirstRound(ChatSession session)
        {
            int userCount = 0;
            foreach (ChatMessage message in session.Messages)
            {
                if (message.Role == "user")
                {
                    userCount++;
                }
            }
            return userCount == 1;
        }

        private static string FirstUserContent(ChatSession session)
        {
            foreach (ChatMessage message in session.Messages)
            {
                if (message.Role == "user")
                {
                    return message.Content.Trim();
                }
            }
            return string.Empty;
        }

        private void ApplyTitle(ChatSession session, string title)
        {
            title = title.Trim().Trim('"', '“', '”', '\'', '。', ' ');
            if (title.Length == 0 || title == session.Title)
            {
                return;
            }

            session.Title = title;
            session.UpdatedAt = DateTime.Now;
            SaveSession(session);
            RefreshListText();
        }

        // ==================== 流式解析与显示 ====================

        // 解析流式分片，取出 choices[0].delta。
        private Dictionary<string, object> ExtractDelta(string json)
        {
            var root = _serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null)
            {
                return null;
            }

            var choices = GetValue(root, "choices") as object[];
            if (choices == null || choices.Length == 0)
            {
                return null;
            }

            var choice = choices[0] as Dictionary<string, object>;
            if (choice == null)
            {
                return null;
            }

            return GetValue(choice, "delta") as Dictionary<string, object>;
        }

        private static string ReadString(Dictionary<string, object> dict, string key)
        {
            object value = GetValue(dict, key);
            return value == null ? string.Empty : Convert.ToString(value);
        }

        private static string AsString(object value)
        {
            return value == null ? string.Empty : Convert.ToString(value);
        }

        private static object GetValue(Dictionary<string, object> dict, string key)
        {
            object value;
            return dict.TryGetValue(key, out value) ? value : null;
        }

        private void AppendReasoning(string text)
        {
            _reasoning.Append(text);
            UpdateThinkingBox();
        }

        // 单行展示：换行符替换成字面量 \r\n，右对齐以便露出最新内容。
        private void UpdateThinkingBox()
        {
            string raw = _reasoning.ToString();

            // 末尾单独一个 \r 可能是被切开的 \r\n，先藏起来等下一片再处理。
            if (raw.EndsWith("\r", StringComparison.Ordinal))
            {
                raw = raw.Substring(0, raw.Length - 1);
            }

            string display = raw
                .Replace("\r\n", "\\r\\n")
                .Replace("\r", "\\r\\n")
                .Replace("\n", "\\r\\n");

            if (_thinkingBox.Text == display)
            {
                return;
            }

            // 完整保存文本，把插入点移到末尾并滚动过去，右侧露出最新内容。
            _thinkingBox.Text = display;
            _thinkingBox.SelectionStart = _thinkingBox.TextLength;
            _thinkingBox.SelectionLength = 0;
            SendMessage(_thinkingBox.Handle, EmScrollCaret, IntPtr.Zero, IntPtr.Zero);
        }

        private const int EmScrollCaret = 0x00B7;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private void AppendContent(string text)
        {
            if (_thinkingTimer.Enabled)
            {
                _thinkingTimer.Stop();
                _statusLabel.Text = string.Empty;
            }

            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            _lastReply.Append(normalized);
            _pending.Append(normalized);

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
            MoveReplyCaretToEnd();
        }

        private void MoveReplyCaretToEnd()
        {
            _replyBox.SelectionStart = _replyBox.TextLength;
            _replyBox.SelectionLength = 0;
            _replyBox.ScrollToCaret();
        }

        private static string FormatSpeaker(string role, DateTime created)
        {
            return (role == "user" ? "你" : "AI") + "  |  "
                + created.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static string BuildTranscript(ChatSession session)
        {
            var builder = new StringBuilder();
            foreach (ChatMessage message in session.Messages)
            {
                if (builder.Length > 0)
                {
                    builder.Append("\r\n\r\n");
                }
                builder.Append(FormatSpeaker(message.Role, message.CreatedAt));
                builder.Append("\r\n");
                builder.Append(message.Content.Replace("\r\n", "\n").Replace("\n", "\r\n"));
            }
            return builder.ToString();
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
