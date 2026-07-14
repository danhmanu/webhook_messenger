export type MessageSnippet = {
  id: string;
  title: string;
  shortcut: string | null;
  content: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
};

export type MessageSnippetInput = {
  title: string;
  shortcut: string | null;
  content: string;
  isActive: boolean;
};

export type HealthStatus = {
  status: string;
  messengerVerifyTokenConfigured: boolean;
  messengerPageAccessTokenConfigured: boolean;
  messengerAppSecretConfigured: boolean;
  adminTokenConfigured: boolean;
  openAiApiKeyConfigured: boolean;
  graphApiVersion: string;
  openAiModel: string;
};

export type AuthSession = {
  authenticated: boolean;
  accessToken: string | null;
  username: string | null;
  expiresAt: string;
  adminTokenConfigured: boolean;
  adminUsernameConfigured: boolean;
  adminPasswordConfigured: boolean;
};

export type PasswordChangeInput = {
  currentPassword: string;
  newPassword: string;
  confirmPassword: string;
};

export type ConversationSummary = {
  senderId: string;
  displayName: string | null;
  lastMessagePreview: string;
  lastMessageAt: string;
  unreadCount: number;
  updatedAt: string;
};

export type ConversationMessage = {
  id: string;
  senderId: string;
  direction: "inbound" | "outbound";
  source: "user" | "bot" | "admin" | string;
  text: string;
  eventType: string;
  facebookMessageId: string | null;
  createdAt: string;
};

export type AgentMemory = {
  id: string;
  senderId: string;
  memoryType: string;
  content: string;
  importance: number;
  createdAt: string;
  updatedAt: string;
};

export type AgentToolCallLog = {
  id: string;
  senderId: string;
  toolName: string;
  inputJson: string;
  outputJson: string;
  createdAt: string;
};
