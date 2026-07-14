import type {
  AgentMemory,
  AgentToolCallLog,
  AuthSession,
  ConversationMessage,
  ConversationSummary,
  HealthStatus,
  MessageSnippet,
  MessageSnippetInput,
  PasswordChangeInput
} from "./types";

const API_BASE = (import.meta.env.VITE_API_BASE ?? "/api/v1").replace(/\/$/, "");
const TOKEN_STORAGE_KEY = "messenger_admin_token";
const USERNAME_STORAGE_KEY = "messenger_admin_username";

export const getStoredAdminToken = () => localStorage.getItem(TOKEN_STORAGE_KEY) ?? "";

export const setStoredAdminToken = (token: string) => {
  if (token.trim()) {
    localStorage.setItem(TOKEN_STORAGE_KEY, token.trim());
    return;
  }

  localStorage.removeItem(TOKEN_STORAGE_KEY);
};

export const getStoredAdminUsername = () => localStorage.getItem(USERNAME_STORAGE_KEY) ?? "";

export const setStoredAdminUsername = (username: string) => {
  if (username.trim()) {
    localStorage.setItem(USERNAME_STORAGE_KEY, username.trim());
    return;
  }

  localStorage.removeItem(USERNAME_STORAGE_KEY);
};

export const clearStoredAdminSession = () => {
  localStorage.removeItem(TOKEN_STORAGE_KEY);
  localStorage.removeItem(USERNAME_STORAGE_KEY);
};

const request = async <T>(path: string, options: RequestInit = {}, tokenOverride?: string): Promise<T> => {
  const token = tokenOverride ?? getStoredAdminToken();
  const headers = new Headers(options.headers);

  if (!(options.body instanceof FormData)) {
    headers.set("Content-Type", "application/json");
  }

  if (token) {
    headers.set("X-Admin-Token", token);
  }

  const response = await fetch(`${API_BASE}${path}`, {
    ...options,
    headers
  });

  if (response.status === 401) {
    throw new Error("Tên đăng nhập hoặc mật khẩu không đúng");
  }

  if (!response.ok) {
    let message = `API lỗi ${response.status}`;

    try {
      const errorBody = await response.json() as { message?: string };
      message = errorBody.message || message;
    } catch {
      // Keep the generic status message when the API does not return JSON.
    }

    throw new Error(message);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return response.json() as Promise<T>;
};

export const api = {
  login: (username: string, password: string) =>
    request<AuthSession>("/auth/session", {
      method: "POST",
      body: JSON.stringify({ username, password })
    }, ""),
  validateSession: () => request<AuthSession>("/auth/session"),
  logout: () =>
    request<void>("/auth/session", {
      method: "DELETE"
    }),
  changePassword: (input: PasswordChangeInput) =>
    request<{ changed: boolean; message: string }>("/auth/password", {
      method: "PATCH",
      body: JSON.stringify(input)
    }),
  health: () => request<HealthStatus>("/health"),
  listSnippets: () => request<MessageSnippet[]>("/message-snippets"),
  createSnippet: (input: MessageSnippetInput) =>
    request<MessageSnippet>("/message-snippets", {
      method: "POST",
      body: JSON.stringify(input)
    }),
  updateSnippet: (id: string, input: MessageSnippetInput) =>
    request<MessageSnippet>(`/message-snippets/${id}`, {
      method: "PUT",
      body: JSON.stringify(input)
    }),
  setSnippetActive: (id: string, isActive: boolean) =>
    request<MessageSnippet>(`/message-snippets/${id}/activation`, {
      method: "PATCH",
      body: JSON.stringify({ isActive })
    }),
  deleteSnippet: (id: string) =>
    request<void>(`/message-snippets/${id}`, {
      method: "DELETE"
    }),
  listConversations: () => request<ConversationSummary[]>("/conversations"),
  listConversationMessages: (senderId: string) =>
    request<ConversationMessage[]>(`/conversations/${encodeURIComponent(senderId)}/messages`),
  listAgentMemories: (senderId: string) =>
    request<AgentMemory[]>(`/conversations/${encodeURIComponent(senderId)}/agent-memories`),
  listAgentToolCalls: (senderId: string) =>
    request<AgentToolCallLog[]>(`/conversations/${encodeURIComponent(senderId)}/agent-tool-calls`),
  sendConversationMessage: (senderId: string, text: string) =>
    request<ConversationMessage>(`/conversations/${encodeURIComponent(senderId)}/messages`, {
      method: "POST",
      body: JSON.stringify({ text })
    })
};
