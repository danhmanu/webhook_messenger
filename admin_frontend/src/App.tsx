import { FormEvent, useEffect, useMemo, useState } from "react";
import {
  Activity,
  Bot,
  CheckCircle2,
  CircleOff,
  Inbox,
  KeyRound,
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
import { api, getStoredAdminToken, setStoredAdminToken } from "./api";
import type {
  AgentMemory,
  AgentToolCallLog,
  ConversationMessage,
  ConversationSummary,
  HealthStatus,
  MessageSnippet,
  MessageSnippetInput
} from "./types";

type ViewMode = "inbox" | "snippets";
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
  const [status, setStatus] = useState("Dang tai...");
  const [error, setError] = useState<string | null>(null);
  const [isBusy, setIsBusy] = useState(false);
  const [adminToken, setAdminToken] = useState(getStoredAdminToken);

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
    showStatus("Dang dong bo...");

    try {
      await Promise.all([
        loadHealth(),
        loadConversations(preferredSenderId),
        loadSnippets(preferredSnippetId)
      ]);
      showStatus("Da dong bo");
    } catch (loadError) {
      showStatus(loadError instanceof Error ? loadError.message : "Khong tai duoc du lieu", true);
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

  const handleTokenSave = () => {
    setStoredAdminToken(adminToken);
    loadAll();
  };

  const handleSendReply = async (event: FormEvent) => {
    event.preventDefault();

    if (!selectedSenderId || !replyText.trim()) {
      return;
    }

    setIsBusy(true);
    showStatus("Dang gui tra loi...");

    try {
      await api.sendConversationMessage(selectedSenderId, replyText.trim());
      setReplyText("");
      await loadConversations(selectedSenderId);
      showStatus("Da gui tra loi");
    } catch (sendError) {
      showStatus(sendError instanceof Error ? sendError.message : "Khong gui duoc", true);
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
      showStatus("Can nhap tieu de va noi dung", true);
      return;
    }

    setIsBusy(true);
    showStatus("Dang luu doan tin...");

    try {
      const saved = selectedSnippetId
        ? await api.updateSnippet(selectedSnippetId, payload)
        : await api.createSnippet(payload);

      await loadSnippets(saved.id);
      showStatus("Da luu doan tin");
    } catch (saveError) {
      showStatus(saveError instanceof Error ? saveError.message : "Khong luu duoc", true);
    } finally {
      setIsBusy(false);
    }
  };

  const handleSnippetDelete = async () => {
    if (!selectedSnippet || !confirm(`Xoa "${selectedSnippet.title}"?`)) {
      return;
    }

    setIsBusy(true);
    showStatus("Dang xoa...");

    try {
      await api.deleteSnippet(selectedSnippet.id);
      await loadSnippets(null);
      showStatus("Da xoa");
    } catch (deleteError) {
      showStatus(deleteError instanceof Error ? deleteError.message : "Khong xoa duoc", true);
    } finally {
      setIsBusy(false);
    }
  };

  const toggleSnippetActive = async (snippet: MessageSnippet) => {
    setIsBusy(true);
    showStatus("Dang cap nhat...");

    try {
      const updated = await api.setSnippetActive(snippet.id, !snippet.isActive);
      await loadSnippets(updated.id);
      showStatus(updated.isActive ? "Da bat doan tin" : "Da tat doan tin");
    } catch (toggleError) {
      showStatus(toggleError instanceof Error ? toggleError.message : "Khong cap nhat duoc", true);
    } finally {
      setIsBusy(false);
    }
  };

  useEffect(() => {
    loadAll(null, null);
  }, []);

  return (
    <div className="admin-shell">
      <aside className="nav-panel">
        <div className="nav-brand">
          <div className="brand-mark">
            <MessageCircle size={22} />
          </div>
          <div>
            <h1>Messenger Admin</h1>
            <p>{status}</p>
          </div>
        </div>

        <nav className="nav-menu">
          <button className={view === "inbox" ? "active" : ""} type="button" onClick={() => setView("inbox")}>
            <Inbox size={18} />
            Hop thu
            {unreadCount > 0 && <span>{unreadCount}</span>}
          </button>
          <button className={view === "snippets" ? "active" : ""} type="button" onClick={() => setView("snippets")}>
            <MessageSquareText size={18} />
            Tin mau
            <span>{activeSnippetCount}</span>
          </button>
        </nav>

        <div className="system-card">
          <div className="card-title">
            <Activity size={18} />
            He thong
          </div>
          <HealthItem label="OpenAI" ready={health?.openAiApiKeyConfigured} />
          <HealthItem label="Page token" ready={health?.messengerPageAccessTokenConfigured} />
          <HealthItem label="Verify token" ready={health?.messengerVerifyTokenConfigured} />
          <HealthItem label="App secret" ready={health?.messengerAppSecretConfigured} />
        </div>

        <div className="token-card">
          <label>
            <span>Admin token</span>
            <input value={adminToken} type="password" onChange={(event) => setAdminToken(event.target.value)} />
          </label>
          <button className="secondary-button full-width" type="button" onClick={handleTokenSave}>
            <KeyRound size={18} />
            Luu token
          </button>
        </div>
      </aside>

      <main className="main-panel">
        <header className="topbar">
          <div>
            <h2>{view === "inbox" ? "Hop thu Messenger" : "Tin nhan mau"}</h2>
            <p>{view === "inbox" ? `${conversations.length} nguoi dung da nhan tin` : `${snippets.length} doan, ${activeSnippetCount} dang bat`}</p>
          </div>
          <button className="icon-button" type="button" onClick={() => loadAll()} disabled={isBusy} title="Tai lai">
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
          <input value={query} onChange={(event) => onQueryChange(event.target.value)} placeholder="Tim nguoi dung, noi dung" />
        </div>

        <div className="conversation-list">
          {conversations.length === 0 ? (
            <div className="empty-state">Chua co hoi thoai</div>
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
                <p>Sender ID: {selectedConversation.senderId}</p>
              </div>
            </header>

            <div className="message-thread">
              <div className="agent-insights">
                <div>
                  <strong>Memory</strong>
                  {agentMemories.length === 0 ? (
                    <span>Chua co memory</span>
                  ) : (
                    agentMemories.slice(0, 3).map((memory) => (
                      <span key={memory.id}>[{memory.memoryType}] {memory.content}</span>
                    ))
                  )}
                </div>
                <div>
                  <strong>Tool calls</strong>
                  <span>{agentToolCalls.length} lan gan nhat</span>
                </div>
              </div>

              {messages.map((message) => (
                <article key={message.id} className={`message-bubble ${message.direction}`}>
                  <div className="bubble-meta">
                    {message.source === "bot" ? <Bot size={14} /> : message.source === "admin" ? <UserRound size={14} /> : <UserRound size={14} />}
                    <span>{message.source === "user" ? "Khach" : message.source === "bot" ? "Bot" : "Quan tri"}</span>
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
                placeholder="Nhap tin tra loi thu cong"
              />
              <button className="primary-button" type="submit" disabled={isBusy || !replyText.trim()}>
                <Send size={18} />
                Gui
              </button>
            </form>
          </>
        ) : (
          <div className="empty-state">Chon mot hoi thoai de xem tin nhan</div>
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
            <input value={query} onChange={(event) => onQueryChange(event.target.value)} placeholder="Tim tieu de, ma, noi dung" />
          </div>
          <button className="secondary-button" type="button" onClick={onCreate}>
            <Plus size={18} />
            Tao
          </button>
        </div>

        <div className="snippet-list">
          {snippets.length === 0 ? (
            <div className="empty-state">Chua co doan phu hop</div>
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
                <span className={`state-pill ${snippet.isActive ? "on" : "off"}`}>{snippet.isActive ? "Bat" : "Tat"}</span>
              </button>
            ))
          )}
        </div>
      </aside>

      <form className="editor-panel" onSubmit={onSubmit}>
        <label>
          <span>Tieu de</span>
          <input value={form.title} maxLength={120} onChange={(event) => onFormChange((current) => ({ ...current, title: event.target.value }))} />
        </label>

        <label>
          <span>Ma goi nho</span>
          <input value={form.shortcut ?? ""} maxLength={80} onChange={(event) => onFormChange((current) => ({ ...current, shortcut: event.target.value || null }))} />
        </label>

        <label>
          <span>Noi dung</span>
          <textarea value={form.content} maxLength={2000} onChange={(event) => onFormChange((current) => ({ ...current, content: event.target.value }))} />
        </label>

        <div className="snippet-meta-row">
          <button
            className={`toggle-button ${form.isActive ? "enabled" : ""}`}
            type="button"
            onClick={() => onFormChange((current) => ({ ...current, isActive: !current.isActive }))}
          >
            {form.isActive ? <CheckCircle2 size={18} /> : <CircleOff size={18} />}
            {form.isActive ? "Dang bat" : "Dang tat"}
          </button>
          <span>{form.content.length}/2000 ky tu</span>
          <span>Cap nhat: {formatDate(selectedSnippet?.updatedAt)}</span>
        </div>

        <div className="form-footer">
          <div className="topbar-actions">
            <button className="danger-button" type="button" onClick={onDelete} disabled={!selectedSnippet || isBusy}>
              <Trash2 size={18} />
              Xoa
            </button>
            {selectedSnippet && (
              <button className="secondary-button" type="button" onClick={() => onToggleActive(selectedSnippet)} disabled={isBusy}>
                {selectedSnippet.isActive ? <CircleOff size={18} /> : <CheckCircle2 size={18} />}
                {selectedSnippet.isActive ? "Tat nhanh" : "Bat nhanh"}
              </button>
            )}
          </div>
          <button className="primary-button" type="submit" disabled={isBusy}>
            <Save size={18} />
            Luu
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
      <strong className={ready ? "ready" : "missing"}>{ready ? "OK" : "Thieu"}</strong>
    </div>
  );
}
