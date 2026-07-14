import { FormEvent, useEffect, useMemo, useState } from "react";
import {
  Activity,
  Bot,
  CheckCircle2,
  CircleOff,
  Inbox,
  LockKeyhole,
  LogOut,
  MessageCircle,
  MessageSquareText,
  Plus,
  RefreshCw,
  Save,
  Search,
  Send,
  Trash2,
  UserRound
} from "lucide-react";
import {
  api,
  clearStoredAdminSession,
  getStoredAdminToken,
  getStoredAdminUsername,
  setStoredAdminToken,
  setStoredAdminUsername
} from "./api";
import type {
  AgentMemory,
  AgentToolCallLog,
  ConversationMessage,
  ConversationSummary,
  HealthStatus,
  MessageSnippet,
  MessageSnippetInput,
  PasswordChangeInput
} from "./types";

type ViewMode = "inbox" | "snippets";
type AuthState = "checking" | "signedOut" | "signedIn";
type SnippetForm = MessageSnippetInput;

const emptySnippetForm: SnippetForm = {
  title: "",
  shortcut: null,
  content: "",
  isActive: true
};

const formatDate = (value?: string) => {
  if (!value) {
    return "-";
  }

  return new Intl.DateTimeFormat("vi-VN", {
    dateStyle: "short",
    timeStyle: "short"
  }).format(new Date(value));
};

const formatTime = (value?: string) => {
  if (!value) {
    return "";
  }

  return new Intl.DateTimeFormat("vi-VN", {
    hour: "2-digit",
    minute: "2-digit",
    day: "2-digit",
    month: "2-digit"
  }).format(new Date(value));
};

export function App() {
  const [view, setView] = useState<ViewMode>("inbox");
  const [health, setHealth] = useState<HealthStatus | null>(null);
  const [status, setStatus] = useState("Đang tải...");
  const [error, setError] = useState<string | null>(null);
  const [isBusy, setIsBusy] = useState(false);
  const [authState, setAuthState] = useState<AuthState>(getStoredAdminToken() ? "checking" : "signedOut");
  const [currentUsername, setCurrentUsername] = useState(getStoredAdminUsername);
  const [sessionExpiresAt, setSessionExpiresAt] = useState<string | null>(null);
  const [loginForm, setLoginForm] = useState({
    username: getStoredAdminUsername(),
    password: "",
    remember: true
  });
  const [loginError, setLoginError] = useState<string | null>(null);
  const [isPasswordFormOpen, setIsPasswordFormOpen] = useState(false);
  const [passwordForm, setPasswordForm] = useState<PasswordChangeInput>({
    currentPassword: "",
    newPassword: "",
    confirmPassword: ""
  });

  const [conversations, setConversations] = useState<ConversationSummary[]>([]);
  const [selectedSenderId, setSelectedSenderId] = useState<string | null>(null);
  const [messages, setMessages] = useState<ConversationMessage[]>([]);
  const [agentMemories, setAgentMemories] = useState<AgentMemory[]>([]);
  const [agentToolCalls, setAgentToolCalls] = useState<AgentToolCallLog[]>([]);
  const [conversationQuery, setConversationQuery] = useState("");
  const [replyText, setReplyText] = useState("");

  const [snippets, setSnippets] = useState<MessageSnippet[]>([]);
  const [selectedSnippetId, setSelectedSnippetId] = useState<string | null>(null);
  const [snippetForm, setSnippetForm] = useState<SnippetForm>(emptySnippetForm);
  const [snippetQuery, setSnippetQuery] = useState("");

  const selectedConversation = conversations.find((item) => item.senderId === selectedSenderId) ?? null;
  const selectedSnippet = snippets.find((snippet) => snippet.id === selectedSnippetId) ?? null;
  const activeSnippetCount = snippets.filter((snippet) => snippet.isActive).length;
  const unreadCount = conversations.reduce((total, item) => total + item.unreadCount, 0);

  const filteredConversations = useMemo(() => {
    const keyword = conversationQuery.trim().toLowerCase();

    if (!keyword) {
      return conversations;
    }

    return conversations.filter((item) =>
      [item.senderId, item.displayName ?? "", item.lastMessagePreview]
        .join(" ")
        .toLowerCase()
        .includes(keyword)
    );
  }, [conversationQuery, conversations]);

  const filteredSnippets = useMemo(() => {
    const keyword = snippetQuery.trim().toLowerCase();

    if (!keyword) {
      return snippets;
    }

    return snippets.filter((snippet) =>
      [snippet.title, snippet.shortcut ?? "", snippet.content]
        .join(" ")
        .toLowerCase()
        .includes(keyword)
    );
  }, [snippetQuery, snippets]);

  const showStatus = (message: string, isError = false) => {
    setStatus(message);
    setError(isError ? message : null);
  };

  const loadHealth = async () => {
    setHealth(await api.health());
  };

  const loadSnippets = async (preferredId?: string | null) => {
    const nextSnippets = await api.listSnippets();
    setSnippets(nextSnippets);

    const nextSelectedId =
      preferredId && nextSnippets.some((snippet) => snippet.id === preferredId)
        ? preferredId
        : nextSnippets[0]?.id ?? null;

    if (nextSelectedId) {
      selectSnippet(nextSnippets, nextSelectedId);
    } else {
      createSnippet();
    }
  };

  const loadConversations = async (preferredSenderId?: string | null) => {
    const nextConversations = await api.listConversations();
    setConversations(nextConversations);

    const nextSenderId =
      preferredSenderId && nextConversations.some((item) => item.senderId === preferredSenderId)
        ? preferredSenderId
        : nextConversations[0]?.senderId ?? null;

    setSelectedSenderId(nextSenderId);

    if (nextSenderId) {
      const [nextMessages, nextMemories, nextToolCalls] = await Promise.all([
        api.listConversationMessages(nextSenderId),
        api.listAgentMemories(nextSenderId),
        api.listAgentToolCalls(nextSenderId)
      ]);
      setMessages(nextMessages);
      setAgentMemories(nextMemories);
      setAgentToolCalls(nextToolCalls);
    } else {
      setMessages([]);
      setAgentMemories([]);
      setAgentToolCalls([]);
    }
  };

  const loadAll = async (preferredSenderId = selectedSenderId, preferredSnippetId = selectedSnippetId) => {
    setIsBusy(true);
    showStatus("Đang đồng bộ...");

    try {
      await Promise.all([
        loadHealth(),
        loadConversations(preferredSenderId),
        loadSnippets(preferredSnippetId)
      ]);
      showStatus("Đã đồng bộ");
    } catch (loadError) {
      showStatus(loadError instanceof Error ? loadError.message : "Không tải được dữ liệu", true);
    } finally {
      setIsBusy(false);
    }
  };

  const selectConversation = async (senderId: string) => {
    setSelectedSenderId(senderId);
    const [nextMessages, nextMemories, nextToolCalls] = await Promise.all([
      api.listConversationMessages(senderId),
      api.listAgentMemories(senderId),
      api.listAgentToolCalls(senderId)
    ]);
    setMessages(nextMessages);
    setAgentMemories(nextMemories);
    setAgentToolCalls(nextToolCalls);
    await loadConversations(senderId);
  };

  const selectSnippet = (source: MessageSnippet[], id: string) => {
    const snippet = source.find((item) => item.id === id);

    if (!snippet) {
      createSnippet();
      return;
    }

    setSelectedSnippetId(snippet.id);
    setSnippetForm({
      title: snippet.title,
      shortcut: snippet.shortcut,
      content: snippet.content,
      isActive: snippet.isActive
    });
  };

  const createSnippet = () => {
    setSelectedSnippetId(null);
    setSnippetForm(emptySnippetForm);
  };

  const handleLogin = async (event: FormEvent) => {
    event.preventDefault();

    if (!loginForm.username.trim() || !loginForm.password) {
      setLoginError("Vui lòng nhập tên đăng nhập và mật khẩu");
      return;
    }

    setIsBusy(true);
    setLoginError(null);

    try {
      const session = await api.login(loginForm.username.trim(), loginForm.password);

      if (!session.accessToken) {
        throw new Error("Backend chưa cấu hình admin token");
      }

      setStoredAdminToken(session.accessToken);
      setStoredAdminUsername(loginForm.remember ? loginForm.username.trim() : "");
      setCurrentUsername(session.username ?? loginForm.username.trim());
      setSessionExpiresAt(session.expiresAt);
      setLoginForm((current) => ({ ...current, password: "" }));
      setAuthState("signedIn");
      await loadAll(null, null);
    } catch (loginException) {
      clearStoredAdminSession();
      setAuthState("signedOut");
      setLoginError(loginException instanceof Error ? loginException.message : "Đăng nhập thất bại");
    } finally {
      setIsBusy(false);
    }
  };

  const handleLogout = async () => {
    try {
      await api.logout();
    } catch {
      // Local logout should still work when the server session is already gone.
    }

    clearStoredAdminSession();
    setAuthState("signedOut");
    setCurrentUsername("");
    setSessionExpiresAt(null);
    setHealth(null);
    setConversations([]);
    setMessages([]);
    setAgentMemories([]);
    setAgentToolCalls([]);
    setSnippets([]);
    showStatus("Đã đăng xuất");
  };

  const handlePasswordChange = async (event: FormEvent) => {
    event.preventDefault();

    if (passwordForm.newPassword.length < 8) {
      showStatus("Mật khẩu mới phải có ít nhất 8 ký tự", true);
      return;
    }

    if (passwordForm.newPassword !== passwordForm.confirmPassword) {
      showStatus("Mật khẩu xác nhận không khớp", true);
      return;
    }

    setIsBusy(true);

    try {
      await api.changePassword(passwordForm);
      setPasswordForm({
        currentPassword: "",
        newPassword: "",
        confirmPassword: ""
      });
      clearStoredAdminSession();
      setSessionExpiresAt(null);
      setAuthState("signedOut");
      showStatus("Đã đổi mật khẩu. Vui lòng đăng nhập lại.");
    } catch (changeError) {
      showStatus(changeError instanceof Error ? changeError.message : "Không đổi được mật khẩu", true);
    } finally {
      setIsBusy(false);
    }
  };

  const handleSendReply = async (event: FormEvent) => {
    event.preventDefault();

    if (!selectedSenderId || !replyText.trim()) {
      return;
    }

    setIsBusy(true);
    showStatus("Đang gửi trả lời...");

    try {
      await api.sendConversationMessage(selectedSenderId, replyText.trim());
      setReplyText("");
      await loadConversations(selectedSenderId);
      showStatus("Đã gửi trả lời");
    } catch (sendError) {
      showStatus(sendError instanceof Error ? sendError.message : "Không gửi được", true);
    } finally {
      setIsBusy(false);
    }
  };

  const handleSnippetSubmit = async (event: FormEvent) => {
    event.preventDefault();

    const payload: MessageSnippetInput = {
      title: snippetForm.title.trim(),
      shortcut: snippetForm.shortcut?.trim() || null,
      content: snippetForm.content.trim(),
      isActive: snippetForm.isActive
    };

    if (!payload.title || !payload.content) {
      showStatus("Cần nhập tiêu đề và nội dung", true);
      return;
    }

    setIsBusy(true);
    showStatus("Đang lưu đoạn tin...");

    try {
      const saved = selectedSnippetId
        ? await api.updateSnippet(selectedSnippetId, payload)
        : await api.createSnippet(payload);

      await loadSnippets(saved.id);
      showStatus("Đã lưu đoạn tin");
    } catch (saveError) {
      showStatus(saveError instanceof Error ? saveError.message : "Không lưu được", true);
    } finally {
      setIsBusy(false);
    }
  };

  const handleSnippetDelete = async () => {
    if (!selectedSnippet || !confirm(`Xóa "${selectedSnippet.title}"?`)) {
      return;
    }

    setIsBusy(true);
    showStatus("Đang xóa...");

    try {
      await api.deleteSnippet(selectedSnippet.id);
      await loadSnippets(null);
      showStatus("Đã xóa");
    } catch (deleteError) {
      showStatus(deleteError instanceof Error ? deleteError.message : "Không xóa được", true);
    } finally {
      setIsBusy(false);
    }
  };

  const toggleSnippetActive = async (snippet: MessageSnippet) => {
    setIsBusy(true);
    showStatus("Đang cập nhật...");

    try {
      const updated = await api.setSnippetActive(snippet.id, !snippet.isActive);
      await loadSnippets(updated.id);
      showStatus(updated.isActive ? "Đã bật đoạn tin" : "Đã tắt đoạn tin");
    } catch (toggleError) {
      showStatus(toggleError instanceof Error ? toggleError.message : "Không cập nhật được", true);
    } finally {
      setIsBusy(false);
    }
  };

  useEffect(() => {
    const validateStoredSession = async () => {
      if (!getStoredAdminToken()) {
        setAuthState("signedOut");
        showStatus("Cần đăng nhập");
        return;
      }

      setAuthState("checking");

      try {
        const session = await api.validateSession();
        setCurrentUsername(session.username ?? getStoredAdminUsername());
        setSessionExpiresAt(session.expiresAt);
        setAuthState("signedIn");
        await loadAll(null, null);
      } catch {
        clearStoredAdminSession();
        setAuthState("signedOut");
        showStatus("Phiên đăng nhập đã hết hạn", true);
      }
    };

    validateStoredSession();
  }, []);

  if (authState !== "signedIn") {
    return (
      <LoginView
        form={loginForm}
        error={loginError ?? error}
        isBusy={isBusy || authState === "checking"}
        isChecking={authState === "checking"}
        onFormChange={setLoginForm}
        onSubmit={handleLogin}
      />
    );
  }

  return (
    <div className="admin-shell">
      <aside className="nav-panel">
        <div className="nav-brand">
          <div className="brand-mark">
            <MessageCircle size={22} />
          </div>
          <div>
            <h1>Quản trị Messenger</h1>
            <p>{status}</p>
          </div>
        </div>

        <nav className="nav-menu">
          <button className={view === "inbox" ? "active" : ""} type="button" onClick={() => setView("inbox")}>
            <Inbox size={18} />
            Hộp thư
            {unreadCount > 0 && <span>{unreadCount}</span>}
          </button>
          <button className={view === "snippets" ? "active" : ""} type="button" onClick={() => setView("snippets")}>
            <MessageSquareText size={18} />
            Tin mẫu
            <span>{activeSnippetCount}</span>
          </button>
        </nav>

        <div className="system-card">
          <div className="card-title">
            <Activity size={18} />
            Hệ thống
          </div>
          <HealthItem label="OpenAI" ready={health?.openAiApiKeyConfigured} />
          <HealthItem label="Token trang" ready={health?.messengerPageAccessTokenConfigured} />
          <HealthItem label="Token xác minh" ready={health?.messengerVerifyTokenConfigured} />
          <HealthItem label="Khóa ứng dụng" ready={health?.messengerAppSecretConfigured} />
        </div>

        <div className="session-card">
          <div className="card-title">
            <UserRound size={18} />
            Tài khoản
          </div>
          <p>{currentUsername || "admin"}</p>
          {sessionExpiresAt && <p>Phiên hết hạn: {formatDate(sessionExpiresAt)}</p>}
          <button className="secondary-button full-width" type="button" onClick={() => setIsPasswordFormOpen((current) => !current)}>
            <LockKeyhole size={18} />
            Đổi mật khẩu
          </button>
          {isPasswordFormOpen && (
            <form className="password-form" onSubmit={handlePasswordChange}>
              <label>
                <span>Mật khẩu hiện tại</span>
                <input
                  type="password"
                  value={passwordForm.currentPassword}
                  onChange={(event) => setPasswordForm((current) => ({ ...current, currentPassword: event.target.value }))}
                />
              </label>
              <label>
                <span>Mật khẩu mới</span>
                <input
                  type="password"
                  value={passwordForm.newPassword}
                  onChange={(event) => setPasswordForm((current) => ({ ...current, newPassword: event.target.value }))}
                />
              </label>
              <label>
                <span>Xác nhận mật khẩu</span>
                <input
                  type="password"
                  value={passwordForm.confirmPassword}
                  onChange={(event) => setPasswordForm((current) => ({ ...current, confirmPassword: event.target.value }))}
                />
              </label>
              <button className="primary-button full-width" type="submit" disabled={isBusy}>
                <Save size={18} />
                Lưu mật khẩu
              </button>
            </form>
          )}
          <button className="secondary-button full-width" type="button" onClick={handleLogout}>
            <LogOut size={18} />
            Đăng xuất
          </button>
        </div>
      </aside>

      <main className="main-panel">
        <header className="topbar">
          <div>
            <h2>{view === "inbox" ? "Hộp thư Messenger" : "Tin nhắn mẫu"}</h2>
            <p>{view === "inbox" ? `${conversations.length} người dùng đã nhắn tin` : `${snippets.length} đoạn, ${activeSnippetCount} đang bật`}</p>
          </div>
          <button className="icon-button" type="button" onClick={() => loadAll()} disabled={isBusy} title="Tải lại">
            <RefreshCw size={18} />
          </button>
        </header>

        {error && <div className="alert">{error}</div>}

        {view === "inbox" ? (
          <InboxView
            conversations={filteredConversations}
            selectedConversation={selectedConversation}
            selectedSenderId={selectedSenderId}
            messages={messages}
            agentMemories={agentMemories}
            agentToolCalls={agentToolCalls}
            query={conversationQuery}
            replyText={replyText}
            isBusy={isBusy}
            onQueryChange={setConversationQuery}
            onSelectConversation={selectConversation}
            onReplyTextChange={setReplyText}
            onSendReply={handleSendReply}
          />
        ) : (
          <SnippetsView
            snippets={filteredSnippets}
            selectedSnippet={selectedSnippet}
            selectedSnippetId={selectedSnippetId}
            form={snippetForm}
            query={snippetQuery}
            isBusy={isBusy}
            onQueryChange={setSnippetQuery}
            onCreate={createSnippet}
            onSelect={(id) => selectSnippet(snippets, id)}
            onFormChange={setSnippetForm}
            onSubmit={handleSnippetSubmit}
            onDelete={handleSnippetDelete}
            onToggleActive={toggleSnippetActive}
          />
        )}
      </main>
    </div>
  );
}

function LoginView({
  form,
  error,
  isBusy,
  isChecking,
  onFormChange,
  onSubmit
}: {
  form: { username: string; password: string; remember: boolean };
  error: string | null;
  isBusy: boolean;
  isChecking: boolean;
  onFormChange: (value: { username: string; password: string; remember: boolean } | ((current: { username: string; password: string; remember: boolean }) => { username: string; password: string; remember: boolean })) => void;
  onSubmit: (event: FormEvent) => void;
}) {
  return (
    <main className="login-shell">
      <section className="login-panel">
        <div className="login-brand">
          <div className="brand-mark">
            <MessageCircle size={24} />
          </div>
          <div>
            <h1>Quản trị Messenger</h1>
            <p>{isChecking ? "Đang kiểm tra phiên đăng nhập" : "Đăng nhập bằng tài khoản quản trị"}</p>
          </div>
        </div>

        <form className="login-form" onSubmit={onSubmit}>
          <label>
            <span>Tên đăng nhập</span>
            <div className="input-with-icon">
              <UserRound size={18} />
              <input
                autoComplete="username"
                value={form.username}
                onChange={(event) => onFormChange((current) => ({ ...current, username: event.target.value }))}
                placeholder="admin"
              />
            </div>
          </label>

          <label>
            <span>Mật khẩu</span>
            <div className="input-with-icon">
              <LockKeyhole size={18} />
              <input
                autoComplete="current-password"
                type="password"
                value={form.password}
                onChange={(event) => onFormChange((current) => ({ ...current, password: event.target.value }))}
                placeholder="Nhập mật khẩu"
              />
            </div>
          </label>

          <label className="remember-row">
            <input
              checked={form.remember}
              type="checkbox"
              onChange={(event) => onFormChange((current) => ({ ...current, remember: event.target.checked }))}
            />
            <span>Ghi nhớ tên đăng nhập trên thiết bị này</span>
          </label>

          {error && <div className="alert compact">{error}</div>}

          <button className="primary-button full-width" type="submit" disabled={isBusy}>
            <LockKeyhole size={18} />
            {isChecking ? "Đang kiểm tra..." : "Đăng nhập"}
          </button>
        </form>
      </section>
    </main>
  );
}

function InboxView({
  conversations,
  selectedConversation,
  selectedSenderId,
  messages,
  agentMemories,
  agentToolCalls,
  query,
  replyText,
  isBusy,
  onQueryChange,
  onSelectConversation,
  onReplyTextChange,
  onSendReply
}: {
  conversations: ConversationSummary[];
  selectedConversation: ConversationSummary | null;
  selectedSenderId: string | null;
  messages: ConversationMessage[];
  agentMemories: AgentMemory[];
  agentToolCalls: AgentToolCallLog[];
  query: string;
  replyText: string;
  isBusy: boolean;
  onQueryChange: (value: string) => void;
  onSelectConversation: (senderId: string) => void;
  onReplyTextChange: (value: string) => void;
  onSendReply: (event: FormEvent) => void;
}) {
  return (
    <section className="inbox-layout">
      <aside className="conversation-panel">
        <div className="search-box">
          <Search size={17} />
          <input value={query} onChange={(event) => onQueryChange(event.target.value)} placeholder="Tìm người dùng, nội dung" />
        </div>

        <div className="conversation-list">
          {conversations.length === 0 ? (
            <div className="empty-state">Chưa có hội thoại</div>
          ) : (
            conversations.map((conversation) => (
              <button
                key={conversation.senderId}
                type="button"
                className={`conversation-row ${conversation.senderId === selectedSenderId ? "selected" : ""}`}
                onClick={() => onSelectConversation(conversation.senderId)}
              >
                <div className="avatar">
                  <UserRound size={18} />
                </div>
                <span>
                  <strong>{conversation.displayName || conversation.senderId}</strong>
                  <small>{conversation.lastMessagePreview}</small>
                </span>
                <time>{formatTime(conversation.lastMessageAt)}</time>
                {conversation.unreadCount > 0 && <em>{conversation.unreadCount}</em>}
              </button>
            ))
          )}
        </div>
      </aside>

      <section className="chat-panel">
        {selectedConversation ? (
          <>
            <header className="chat-header">
              <div className="avatar large">
                <UserRound size={20} />
              </div>
              <div>
                <h3>{selectedConversation.displayName || selectedConversation.senderId}</h3>
                <p>Mã người gửi: {selectedConversation.senderId}</p>
              </div>
            </header>

            <div className="message-thread">
              <div className="agent-insights">
                <div>
                  <strong>Bộ nhớ</strong>
                  {agentMemories.length === 0 ? (
                    <span>Chưa có bộ nhớ</span>
                  ) : (
                    agentMemories.slice(0, 3).map((memory) => (
                      <span key={memory.id}>[{memory.memoryType}] {memory.content}</span>
                    ))
                  )}
                </div>
                <div>
                  <strong>Lượt gọi công cụ</strong>
                  <span>{agentToolCalls.length} lần gần nhất</span>
                </div>
              </div>

              {messages.map((message) => (
                <article key={message.id} className={`message-bubble ${message.direction}`}>
                  <div className="bubble-meta">
                    {message.source === "bot" ? <Bot size={14} /> : message.source === "admin" ? <UserRound size={14} /> : <UserRound size={14} />}
                    <span>{message.source === "user" ? "Khách" : message.source === "bot" ? "Bot" : "Quản trị"}</span>
                    <time>{formatTime(message.createdAt)}</time>
                  </div>
                  <p>{message.text}</p>
                </article>
              ))}
            </div>

            <form className="reply-box" onSubmit={onSendReply}>
              <textarea
                value={replyText}
                onChange={(event) => onReplyTextChange(event.target.value)}
                placeholder="Nhập tin trả lời thủ công"
              />
              <button className="primary-button" type="submit" disabled={isBusy || !replyText.trim()}>
                <Send size={18} />
                Gửi
              </button>
            </form>
          </>
        ) : (
          <div className="empty-state">Chọn một hội thoại để xem tin nhắn</div>
        )}
      </section>
    </section>
  );
}

function SnippetsView({
  snippets,
  selectedSnippet,
  selectedSnippetId,
  form,
  query,
  isBusy,
  onQueryChange,
  onCreate,
  onSelect,
  onFormChange,
  onSubmit,
  onDelete,
  onToggleActive
}: {
  snippets: MessageSnippet[];
  selectedSnippet: MessageSnippet | null;
  selectedSnippetId: string | null;
  form: SnippetForm;
  query: string;
  isBusy: boolean;
  onQueryChange: (value: string) => void;
  onCreate: () => void;
  onSelect: (id: string) => void;
  onFormChange: (value: SnippetForm | ((current: SnippetForm) => SnippetForm)) => void;
  onSubmit: (event: FormEvent) => void;
  onDelete: () => void;
  onToggleActive: (snippet: MessageSnippet) => void;
}) {
  return (
    <section className="snippet-layout">
      <aside className="snippet-sidebar">
        <div className="snippet-sidebar-actions">
          <div className="search-box">
            <Search size={17} />
            <input value={query} onChange={(event) => onQueryChange(event.target.value)} placeholder="Tìm tiêu đề, mã, nội dung" />
          </div>
          <button className="secondary-button" type="button" onClick={onCreate}>
            <Plus size={18} />
            Tạo
          </button>
        </div>

        <div className="snippet-list">
          {snippets.length === 0 ? (
            <div className="empty-state">Chưa có đoạn phù hợp</div>
          ) : (
            snippets.map((snippet) => (
              <button
                key={snippet.id}
                type="button"
                className={`snippet-row ${snippet.id === selectedSnippetId ? "selected" : ""}`}
                onClick={() => onSelect(snippet.id)}
              >
                <span>
                  <strong>{snippet.title}</strong>
                  <small>{snippet.shortcut || snippet.content}</small>
                </span>
                <span className={`state-pill ${snippet.isActive ? "on" : "off"}`}>{snippet.isActive ? "Bật" : "Tắt"}</span>
              </button>
            ))
          )}
        </div>
      </aside>

      <form className="editor-panel" onSubmit={onSubmit}>
        <label>
          <span>Tiêu đề</span>
          <input value={form.title} maxLength={120} onChange={(event) => onFormChange((current) => ({ ...current, title: event.target.value }))} />
        </label>

        <label>
          <span>Mã gợi nhớ</span>
          <input value={form.shortcut ?? ""} maxLength={80} onChange={(event) => onFormChange((current) => ({ ...current, shortcut: event.target.value || null }))} />
        </label>

        <label>
          <span>Nội dung</span>
          <textarea value={form.content} maxLength={2000} onChange={(event) => onFormChange((current) => ({ ...current, content: event.target.value }))} />
        </label>

        <div className="snippet-meta-row">
          <button
            className={`toggle-button ${form.isActive ? "enabled" : ""}`}
            type="button"
            onClick={() => onFormChange((current) => ({ ...current, isActive: !current.isActive }))}
          >
            {form.isActive ? <CheckCircle2 size={18} /> : <CircleOff size={18} />}
            {form.isActive ? "Đang bật" : "Đang tắt"}
          </button>
          <span>{form.content.length}/2000 ký tự</span>
          <span>Cập nhật: {formatDate(selectedSnippet?.updatedAt)}</span>
        </div>

        <div className="form-footer">
          <div className="topbar-actions">
            <button className="danger-button" type="button" onClick={onDelete} disabled={!selectedSnippet || isBusy}>
              <Trash2 size={18} />
              Xóa
            </button>
            {selectedSnippet && (
              <button className="secondary-button" type="button" onClick={() => onToggleActive(selectedSnippet)} disabled={isBusy}>
                {selectedSnippet.isActive ? <CircleOff size={18} /> : <CheckCircle2 size={18} />}
                {selectedSnippet.isActive ? "Tắt nhanh" : "Bật nhanh"}
              </button>
            )}
          </div>
          <button className="primary-button" type="submit" disabled={isBusy}>
            <Save size={18} />
            Lưu
          </button>
        </div>
      </form>
    </section>
  );
}

function HealthItem({ label, ready }: { label: string; ready?: boolean }) {
  return (
    <div className="health-row">
      <span>{label}</span>
      <strong className={ready ? "ready" : "missing"}>{ready ? "OK" : "Thiếu"}</strong>
    </div>
  );
}
