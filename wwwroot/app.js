const chatList = document.querySelector("#chat-list");
const welcome = document.querySelector("#welcome");
const messagesElement = document.querySelector("#messages");
const form = document.querySelector("#chat-form");
const input = document.querySelector("#message-input");
const sendButton = document.querySelector("#send-button");
const errorElement = document.querySelector("#error-message");
const newChatButton = document.querySelector("#new-chat");
const authOverlay = document.querySelector("#auth-overlay");
const authForm = document.querySelector("#auth-form");
const authEmail = document.querySelector("#auth-email");
const authPassword = document.querySelector("#auth-password");
const authError = document.querySelector("#auth-error");
const authSubmit = document.querySelector("#auth-submit");
const authSwitch = document.querySelector("#auth-switch");
const authTitle = document.querySelector("#auth-title");
const authCopy = document.querySelector("#auth-copy");
const accountEmail = document.querySelector("#account-email");
const signOutButton = document.querySelector("#sign-out");
const voiceButton = document.querySelector("#voice-button");
const voiceLanguage = document.querySelector("#voice-language");

let conversations = [];
let activeConversationId = null;
let isSending = false;
let authMode = "login";
let speechRecognition = null;

function activeConversation() {
  return conversations.find((conversation) => conversation.id === activeConversationId);
}

async function readApiResponse(response) {
  if (response.status === 401) {
    showAuth();
    throw new Error("Sign in to continue.");
  }

  const result = response.status === 204 ? null : await response.json();
  if (!response.ok) {
    throw new Error(result?.detail || result?.message || result?.title || "Something went wrong. Please try again.");
  }
  return result;
}

function showAuth(message = "") {
  authOverlay.hidden = false;
  authError.textContent = message;
  authError.hidden = !message;
  authEmail.focus();
}

function hideAuth(email) {
  authOverlay.hidden = true;
  authError.textContent = "";
  authError.hidden = true;
  accountEmail.textContent = email;
  signOutButton.hidden = false;
}

function setAuthMode(mode) {
  authMode = mode;
  const registering = mode === "register";
  authTitle.textContent = registering ? "Create your account" : "Welcome back";
  authCopy.textContent = registering
    ? "Create an account to save your conversations."
    : "Sign in to continue your conversations.";
  authPassword.autocomplete = registering ? "new-password" : "current-password";
  authSubmit.textContent = registering ? "Create account" : "Sign in";
  authSwitch.replaceChildren();

  const prompt = document.createElement("span");
  prompt.textContent = registering ? "Already have an account? " : "New to TalkGPT? ";
  const toggle = document.createElement("button");
  toggle.type = "button";
  toggle.textContent = registering ? "Sign in" : "Create an account";
  toggle.addEventListener("click", () => {
    authForm.reset();
    setAuthMode(registering ? "login" : "register");
  });
  authSwitch.append(prompt, toggle);
}

async function loadConversations() {
  const result = await readApiResponse(await fetch("/api/conversations"));
  conversations = result.map((conversation) => ({
    id: conversation.id,
    title: conversation.title,
    messages: [],
  }));
  activeConversationId = null;
  render();
}

function renderChatList() {
  chatList.replaceChildren();
  if (conversations.length === 0) {
    const empty = document.createElement("div");
    empty.className = "history-empty";
    empty.textContent = "Your conversations will show up here.";
    chatList.append(empty);
    return;
  }

  for (const conversation of [...conversations].reverse()) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = `history-item${conversation.id === activeConversationId ? " active" : ""}`;
    button.setAttribute("aria-current", conversation.id === activeConversationId ? "page" : "false");

    const icon = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    icon.setAttribute("viewBox", "0 0 24 24");
    icon.setAttribute("aria-hidden", "true");
    const path = document.createElementNS("http://www.w3.org/2000/svg", "path");
    path.setAttribute("d", "M20 11.5a7.5 7.5 0 0 1-7.5 7.5H5l1.8-3.6A7.5 7.5 0 1 1 20 11.5Z");
    icon.append(path);

    const title = document.createElement("span");
    title.textContent = conversation.title;
    button.append(icon, title);
    button.addEventListener("click", async () => {
      try {
        const stored = await readApiResponse(await fetch(`/api/conversations/${encodeURIComponent(conversation.id)}`));
        conversation.messages = stored.messages;
        conversation.title = stored.title;
        activeConversationId = conversation.id;
        clearError();
        hideSidebar();
        render();
      } catch (error) {
        showError(error.message);
      }
    });
    chatList.append(button);
  }
}

function appendMessage(role, content) {
  const message = document.createElement("article");
  message.className = `message ${role}`;

  const avatar = document.createElement("div");
  avatar.className = "message-avatar";
  avatar.textContent = role === "assistant" ? "T" : "Y";
  avatar.setAttribute("aria-hidden", "true");

  const body = document.createElement("div");
  body.className = "message-content";
  const header = document.createElement("div");
  header.className = "message-header";
  const name = document.createElement("div");
  name.className = "message-name";
  name.textContent = role === "assistant" ? "TalkGPT" : "You";
  header.append(name);

  if (role === "assistant" && content) {
    const speakButton = document.createElement("button");
    speakButton.type = "button";
    speakButton.className = "speak-button";
    speakButton.title = "Read response aloud";
    speakButton.setAttribute("aria-label", "Read response aloud");
    speakButton.innerHTML = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 10v4h3l4 3V7l-4 3H4Zm11-1a4 4 0 0 1 0 6m2-8a7 7 0 0 1 0 10"/></svg>';
    speakButton.addEventListener("click", () => speakText(content));
    header.append(speakButton);
  }

  const text = document.createElement("div");
  text.className = "message-text";
  text.textContent = content;
  body.append(header, text);
  message.append(avatar, body);
  messagesElement.append(message);
}

function render() {
  renderChatList();
  messagesElement.replaceChildren();
  const conversation = activeConversation();
  const messages = conversation?.messages ?? [];
  welcome.hidden = messages.length > 0;
  for (const message of messages) {
    appendMessage(message.role, message.content);
  }
  messagesElement.scrollTop = messagesElement.scrollHeight;
}

function showError(message) {
  errorElement.textContent = message;
  errorElement.hidden = false;
}

function clearError() {
  errorElement.textContent = "";
  errorElement.hidden = true;
}

function setBusy(busy) {
  isSending = busy;
  sendButton.disabled = busy || input.value.trim().length === 0;
  input.disabled = busy;
  newChatButton.disabled = busy;
  for (const button of chatList.querySelectorAll("button")) {
    button.disabled = busy;
  }
}

function hideSidebar() {
  document.body.classList.remove("sidebar-open");
}

function startNewChat() {
  if (isSending || authOverlay.hidden === false) return;
  activeConversationId = null;
  clearError();
  hideSidebar();
  render();
  input.focus();
}

function speakText(text) {
  if (!("speechSynthesis" in window) || !("SpeechSynthesisUtterance" in window)) {
    showError("Voice playback is not supported in this browser.");
    return;
  }

  window.speechSynthesis.cancel();
  const utterance = new SpeechSynthesisUtterance(text);
  utterance.lang = voiceLanguage.value;
  utterance.onerror = () => showError("Voice playback could not start. Check your device audio settings.");
  window.speechSynthesis.speak(utterance);
}

newChatButton.addEventListener("click", startNewChat);
document.querySelector("#open-sidebar").addEventListener("click", () => {
  document.body.classList.add("sidebar-open");
});
document.querySelector("#close-sidebar").addEventListener("click", hideSidebar);
document.querySelector("#mobile-scrim").addEventListener("click", hideSidebar);

document.querySelector("#suggestions").addEventListener("click", (event) => {
  const button = event.target.closest("[data-prompt]");
  if (!button || isSending) return;
  input.value = button.dataset.prompt;
  input.dispatchEvent(new Event("input"));
  form.requestSubmit();
});

input.addEventListener("input", () => {
  input.style.height = "auto";
  input.style.height = `${Math.min(input.scrollHeight, 180)}px`;
  sendButton.disabled = isSending || input.value.trim().length === 0;
});

input.addEventListener("keydown", (event) => {
  if (event.key === "Enter" && !event.shiftKey) {
    event.preventDefault();
    form.requestSubmit();
  }
});

document.addEventListener("keydown", (event) => {
  if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k") {
    event.preventDefault();
    startNewChat();
  }
  if (event.key === "Escape") hideSidebar();
});

authForm.addEventListener("submit", async (event) => {
  event.preventDefault();
  authError.hidden = true;
  authSubmit.disabled = true;

  try {
    const endpoint = authMode === "register" ? "/api/register" : "/api/login";
    const response = await fetch(endpoint, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email: authEmail.value.trim(), password: authPassword.value }),
    });
    const result = response.status === 204 ? null : await response.json();
    if (!response.ok) {
      throw new Error(result?.message || result?.detail || "Could not sign in. Please try again.");
    }

    hideAuth(result.email);
    authForm.reset();
    clearError();
    await loadConversations();
    input.focus();
  } catch (error) {
    authError.textContent = error instanceof Error ? error.message : "Could not sign in. Please try again.";
    authError.hidden = false;
  } finally {
    authSubmit.disabled = false;
  }
});

signOutButton.addEventListener("click", async () => {
  signOutButton.disabled = true;
  try {
    await readApiResponse(await fetch("/api/logout", { method: "POST" }));
    conversations = [];
    activeConversationId = null;
    render();
    accountEmail.textContent = "Sign in to save your chats.";
    signOutButton.hidden = true;
    setAuthMode("login");
    showAuth();
  } catch (error) {
    showError(error.message);
  } finally {
    signOutButton.disabled = false;
  }
});

voiceButton.addEventListener("click", () => {
  if (speechRecognition) {
    speechRecognition.stop();
    return;
  }

  const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
  if (!Recognition) {
    showError("Voice input is not supported in this browser. Try a recent version of Chrome or Edge.");
    return;
  }

  clearError();
  const recognition = new Recognition();
  speechRecognition = recognition;
  recognition.lang = voiceLanguage.value;
  recognition.continuous = false;
  recognition.interimResults = false;
  voiceButton.classList.add("listening");
  voiceButton.setAttribute("aria-label", "Stop voice input");
  recognition.onresult = (event) => {
    const transcript = Array.from(event.results)
      .slice(event.resultIndex)
      .map((result) => result[0].transcript)
      .join(" ")
      .trim();
    if (transcript) {
      input.value = [input.value.trim(), transcript].filter(Boolean).join(" ");
      input.dispatchEvent(new Event("input"));
      input.focus();
    }
  };
  recognition.onerror = (event) => {
    const message = event.error === "not-allowed"
      ? "Microphone permission was denied. Allow microphone access in your browser settings."
      : event.error === "no-speech"
        ? "No speech was detected. Please try again."
        : "Voice input could not start. Check microphone access and try again.";
    if (event.error !== "aborted") showError(message);
  };
  recognition.onend = () => {
    speechRecognition = null;
    voiceButton.classList.remove("listening");
    voiceButton.setAttribute("aria-label", "Start voice input");
  };

  try {
    recognition.start();
  } catch {
    speechRecognition = null;
    voiceButton.classList.remove("listening");
    voiceButton.setAttribute("aria-label", "Start voice input");
    showError("Voice input could not start. Please try again.");
  }
});

form.addEventListener("submit", async (event) => {
  event.preventDefault();
  const prompt = input.value.trim();
  if (!prompt || isSending) return;

  let conversation = activeConversation();
  if (!conversation) {
    conversation = {
      id: crypto.randomUUID().replaceAll("-", ""),
      title: prompt.length > 100 ? `${prompt.slice(0, 99)}…` : prompt,
      messages: [],
    };
    conversations.push(conversation);
    activeConversationId = conversation.id;
  }

  const previousMessages = conversation.messages;
  input.value = "";
  input.style.height = "auto";
  clearError();
  conversation.messages = [...previousMessages, { role: "user", content: prompt }];
  setBusy(true);
  render();
  appendMessage("assistant", "");

  const typingIndicator = document.createElement("div");
  typingIndicator.className = "typing-dots";
  typingIndicator.setAttribute("aria-label", "TalkGPT is thinking");
  for (let index = 0; index < 3; index++) {
    typingIndicator.append(document.createElement("span"));
  }
  messagesElement.lastElementChild.querySelector(".message-content").append(typingIndicator);

  try {
    const response = await fetch("/api/chat", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        message: prompt,
        history: previousMessages,
        conversationId: conversation.id,
        title: conversation.title,
      }),
    });

    const result = await readApiResponse(response);
    if (typeof result.answer !== "string" || !result.answer.trim()) {
      throw new Error("The AI service returned an empty response. Please try again.");
    }

    conversation.messages.push({ role: "assistant", content: result.answer });
  } catch (error) {
    conversation.messages = previousMessages;
    if (conversation.messages.length === 0) {
      conversations = conversations.filter((item) => item.id !== conversation.id);
      activeConversationId = null;
    }
    showError(error instanceof Error ? error.message : "Something went wrong. Please try again.");
  } finally {
    setBusy(false);
    render();
  }
});

async function initialize() {
  setAuthMode("login");
  render();

  try {
    const account = await readApiResponse(await fetch("/api/account"));
    hideAuth(account.email);
    await loadConversations();
  } catch (error) {
    if (!authOverlay.hidden) return;
    showError(error instanceof Error ? error.message : "Could not load your conversations.");
  }
}

initialize();
