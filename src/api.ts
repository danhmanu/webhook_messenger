import type {
  AgentMemory,
  AgentToolCallLog,
  ConversationMessage,
  ConversationSummary,
  HealthStatus,
  MessageSnippet,
  MessageSnippetInput
} from "./types";

const API_BASE = (import.meta.env.VITE_API_BASE ?? "/api/v1").replace(/\/$/, "");
const TOKEN_STORAGE_KEY = "messenger_admin_token";

export const getStoredAdminToken = () => localStorage.getItem(TOKEN_STORAGE_KEY) ?? "";

export const setStoredAdminToken = (token: string) => {
  if (token.trim()) {
    localStorage.setItem(TOKEN_STORAGE_KEY, token.trim());
    return;
  }

  localStorage.removeItem(TOKEN_STORAGE_KEY);
};

const request = async <T>(path: string, options: RequestInit = {}): Promise<T> => {
  const token = getStoredAdminToken();
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
    throw new Error("Token quan tri khong hop le");
  }

  if (!response.ok) {
    throw new Error(`API loi ${response.status}`);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return response.json() as Promise<T>;
};

export const api = {
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
