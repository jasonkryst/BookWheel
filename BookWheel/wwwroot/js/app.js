const loginView = document.getElementById('loginView');
const appView = document.getElementById('appView');
const loginForm = document.getElementById('loginForm');
const loginError = document.getElementById('loginError');
const authTitle = document.getElementById('authTitle');
const authMessage = document.getElementById('authMessage');
const authSubmitBtn = document.getElementById('authSubmitBtn');
const usernameInput = document.getElementById('username');
const passwordInput = document.getElementById('password');
const resetPasswordForm = document.getElementById('resetPasswordForm');
const resetPassword = document.getElementById('resetPassword');
const resetPasswordConfirm = document.getElementById('resetPasswordConfirm');
const resetPasswordSubmitBtn = document.getElementById('resetPasswordSubmitBtn');
const resetPasswordMessage = document.getElementById('resetPasswordMessage');
const resetPasswordError = document.getElementById('resetPasswordError');
const bookForm = document.getElementById('bookForm');
const bookTitle = document.getElementById('bookTitle');
const bookMessage = document.getElementById('bookMessage');
const activeBooksEl = document.getElementById('activeBooks');
const booksPaginationEl = document.getElementById('booksPagination');
const booksPrevPageBtn = document.getElementById('booksPrevPageBtn');
const booksNextPageBtn = document.getElementById('booksNextPageBtn');
const booksPageInfo = document.getElementById('booksPageInfo');
const booksTotalCountEl = document.getElementById('booksTotalCount');
const selectedBookEl = document.getElementById('selectedBook');
const wheelSummaryEl = document.getElementById('wheelSummary');
const wheelBooksSrListEl = document.getElementById('wheelBooksSrList');
const spinBtn = document.getElementById('spinBtn');
const logoutBtn = document.getElementById('logoutBtn');
const userManagementBtn = document.getElementById('userManagementBtn');
const importExportBtn = document.getElementById('importExportBtn');
const themeToggleBtn = document.getElementById('themeToggleBtn');
const themeToggleIcon = document.getElementById('themeToggleIcon');
const userGreeting = document.getElementById('userGreeting');
const canvas = document.getElementById('wheelCanvas');
const ctx = canvas.getContext('2d');
const editDialog = document.getElementById('editDialog');
const editForm = document.getElementById('editForm');
const editBookId = document.getElementById('editBookId');
const editBookTitle = document.getElementById('editBookTitle');
const editError = document.getElementById('editError');
const cancelEditBtn = document.getElementById('cancelEditBtn');
const deleteDialog = document.getElementById('deleteDialog');
const deleteConfirmMessage = document.getElementById('deleteConfirmMessage');
const deleteError = document.getElementById('deleteError');
const cancelDeleteBtn = document.getElementById('cancelDeleteBtn');
const confirmDeleteBtn = document.getElementById('confirmDeleteBtn');
const deleteUserDialog = document.getElementById('deleteUserDialog');
const deleteUserConfirmMessage = document.getElementById('deleteUserConfirmMessage');
const deleteUserError = document.getElementById('deleteUserError');
const cancelDeleteUserBtn = document.getElementById('cancelDeleteUserBtn');
const confirmDeleteUserBtn = document.getElementById('confirmDeleteUserBtn');
const resetLinkDialog = document.getElementById('resetLinkDialog');
const resetLinkMessage = document.getElementById('resetLinkMessage');
const resetLinkValue = document.getElementById('resetLinkValue');
const resetLinkError = document.getElementById('resetLinkError');
const copyResetLinkBtn = document.getElementById('copyResetLinkBtn');
const closeResetLinkBtn = document.getElementById('closeResetLinkBtn');
const transferDialog = document.getElementById('transferDialog');
const importTabBtn = document.getElementById('importTabBtn');
const exportTabBtn = document.getElementById('exportTabBtn');
const importPanel = document.getElementById('importPanel');
const exportPanel = document.getElementById('exportPanel');
const importJsonFile = document.getElementById('importJsonFile');
const importFileBtn = document.getElementById('importFileBtn');
const downloadExportBtn = document.getElementById('downloadExportBtn');
const cancelTransferBtn = document.getElementById('cancelTransferBtn');
const closeExportBtn = document.getElementById('closeExportBtn');
const transferMessage = document.getElementById('transferMessage');
const transferError = document.getElementById('transferError');
const userManagementDialog = document.getElementById('userManagementDialog');
const closeUserManagementBtn = document.getElementById('closeUserManagementBtn');
const createUserForm = document.getElementById('createUserForm');
const createUserUsername = document.getElementById('createUserUsername');
const createUserIsAdmin = document.getElementById('createUserIsAdmin');
const userSearchInput = document.getElementById('userSearchInput');
const userCountBadge = document.getElementById('userCountBadge');
const userList = document.getElementById('userList');
const userManagementMessage = document.getElementById('userManagementMessage');
const userManagementError = document.getElementById('userManagementError');
const appVersionEl = document.getElementById('appVersion');
const toastRegion = document.getElementById('toastRegion');

let activeBooks = [];
let wheelBooks = [];
let spinning = false;
let currentRotation = 0;
let currentPage = 1;
let authMode = 'login';
let pendingDeleteBook = null;
let pendingDeleteUser = null;
let currentUser = null;
let allUsers = [];
let resetTokenFromUrl = null;
const BOOKS_PER_PAGE = 10;
const THEME_STORAGE_KEY = 'bookwheel-theme';
const DARK_THEME = 'dark';
const LIGHT_THEME = 'light';
const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
const dialogFocusReturnMap = new WeakMap();

function showToast(message, type = 'info') {
  if (!toastRegion || !message) {
    return;
  }

  const toast = document.createElement('div');
  toast.className = `toast toast-${type}`;
  toast.textContent = message;
  toastRegion.appendChild(toast);

  window.setTimeout(() => {
    toast.classList.add('toast-exit');
    window.setTimeout(() => {
      toast.remove();
    }, 260);
  }, 2600);
}

function setButtonBusy(button, isBusy, busyText, idleText) {
  if (!button) {
    return;
  }

  button.disabled = isBusy;
  if (busyText && idleText) {
    button.textContent = isBusy ? busyText : idleText;
  }
}

function shuffleArray(items) {
  for (let i = items.length - 1; i > 0; i -= 1) {
    const j = Math.floor(Math.random() * (i + 1));
    [items[i], items[j]] = [items[j], items[i]];
  }
  return items;
}

function normalizeTitle(title) {
  return title.trim().toLocaleLowerCase();
}

function isReducedMotionEnabled() {
  return Boolean(prefersReducedMotion && prefersReducedMotion.matches);
}

function findFirstFocusable(root) {
  if (!root) {
    return null;
  }

  return root.querySelector('button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])');
}

function openDialog(dialog, preferredFocusElement = null) {
  if (!dialog) {
    return;
  }

  const activeElement = document.activeElement;
  if (activeElement && typeof activeElement.focus === 'function') {
    dialogFocusReturnMap.set(dialog, activeElement);
  }

  dialog.addEventListener('close', () => {
    const restoreFocusElement = dialogFocusReturnMap.get(dialog);
    if (restoreFocusElement && typeof restoreFocusElement.focus === 'function') {
      restoreFocusElement.focus();
    }
    dialogFocusReturnMap.delete(dialog);
  }, { once: true });

  if (typeof dialog.showModal === 'function') {
    dialog.showModal();
  } else {
    dialog.setAttribute('open', 'open');
  }

  const focusTarget = preferredFocusElement || findFirstFocusable(dialog);
  if (focusTarget && typeof focusTarget.focus === 'function') {
    focusTarget.focus();
  }
}

function closeDialog(dialog) {
  if (!dialog) {
    return;
  }

  if (typeof dialog.close === 'function') {
    dialog.close();
    return;
  }

  dialog.removeAttribute('open');
  const restoreFocusElement = dialogFocusReturnMap.get(dialog);
  if (restoreFocusElement && typeof restoreFocusElement.focus === 'function') {
    restoreFocusElement.focus();
  }
  dialogFocusReturnMap.delete(dialog);
}

function renderWheelAccessibilitySummary() {
  if (!wheelSummaryEl || !wheelBooksSrListEl) {
    return;
  }

  wheelBooksSrListEl.innerHTML = '';

  if (!wheelBooks.length) {
    wheelSummaryEl.textContent = 'Wheel is empty. Add books to spin.';
    return;
  }

  wheelSummaryEl.textContent = `Wheel has ${wheelBooks.length} books.`;
  wheelBooks.forEach((book, index) => {
    const item = document.createElement('li');
    item.textContent = `${index + 1}. ${book.title}`;
    wheelBooksSrListEl.appendChild(item);
  });
}

function hasOpenDialog() {
  return Boolean(document.querySelector('dialog[open]'));
}

function shouldHandlePaginationHotkey(event) {
  if (event.altKey || event.ctrlKey || event.metaKey || hasOpenDialog()) {
    return false;
  }

  const target = event.target;
  if (!(target instanceof HTMLElement)) {
    return true;
  }

  if (target.isContentEditable) {
    return false;
  }

  const interactiveParent = target.closest('input, textarea, button, select, [role="dialog"], [role="tablist"], [role="tab"]');
  return !interactiveParent;
}

function setWheelBooksFromActive(shuffleWheel = false) {
  const activeById = new Map(activeBooks.map(book => [book.id, book]));
  const existingIds = new Set();
  const preserved = [];

  wheelBooks.forEach(book => {
    if (!activeById.has(book.id) || existingIds.has(book.id)) {
      return;
    }

    preserved.push(activeById.get(book.id));
    existingIds.add(book.id);
  });

  const additions = activeBooks.filter(book => !existingIds.has(book.id));
  wheelBooks = [...preserved, ...additions];

  if (shuffleWheel) {
    shuffleArray(wheelBooks);
  }

  renderWheelAccessibilitySummary();
}

function getPreferredTheme() {
  const persisted = localStorage.getItem(THEME_STORAGE_KEY);
  if (persisted === DARK_THEME || persisted === LIGHT_THEME) {
    return persisted;
  }

  return window.matchMedia('(prefers-color-scheme: dark)').matches ? DARK_THEME : LIGHT_THEME;
}

function applyTheme(theme) {
  document.documentElement.setAttribute('data-theme', theme);
  localStorage.setItem(THEME_STORAGE_KEY, theme);

  if (themeToggleBtn) {
    const nextThemeLabel = theme === DARK_THEME ? 'Light mode' : 'Dark mode';
    themeToggleBtn.setAttribute('aria-label', `Switch to ${nextThemeLabel}`);
    themeToggleBtn.setAttribute('title', `Switch to ${nextThemeLabel}`);
    if (themeToggleIcon) {
      themeToggleIcon.textContent = theme === DARK_THEME ? '☾' : '☀';
    }
  }

  drawWheel();
}

function toggleTheme() {
  const currentTheme = document.documentElement.getAttribute('data-theme') || DARK_THEME;
  const nextTheme = currentTheme === DARK_THEME ? LIGHT_THEME : DARK_THEME;
  applyTheme(nextTheme);
}

function getTotalPages() {
  return Math.max(1, Math.ceil(activeBooks.length / BOOKS_PER_PAGE));
}

function clampCurrentPage() {
  currentPage = Math.min(Math.max(1, currentPage), getTotalPages());
}

function renderPagination() {
  const totalPages = getTotalPages();
  const hasMultiplePages = activeBooks.length > BOOKS_PER_PAGE;

  booksPaginationEl.classList.toggle('hidden', !hasMultiplePages);
  booksPageInfo.textContent = `Page ${currentPage} of ${totalPages}`;
  booksPrevPageBtn.disabled = !hasMultiplePages || currentPage <= 1;
  booksNextPageBtn.disabled = !hasMultiplePages || currentPage >= totalPages;
}

function renderBookCount() {
  const totalBooks = activeBooks.length;
  const totalPages = getTotalPages();
  const bookLabel = totalBooks === 1 ? '1 book total' : `${totalBooks} books total`;
  booksTotalCountEl.textContent = `${bookLabel} • Page ${currentPage} of ${totalPages}`;
}

function showApp(show) {
  loginView.classList.toggle('hidden', show);
  appView.classList.toggle('hidden', !show);
}

function applyCurrentUser(user) {
  currentUser = user;
  const canManageUsers = Boolean(currentUser && currentUser.isAdmin);

  if (userGreeting) {
    const hasUser = Boolean(currentUser && currentUser.username);
    userGreeting.classList.toggle('hidden', !hasUser);
    userGreeting.textContent = hasUser ? `Hello, ${currentUser.username}` : '';
  }

  if (userManagementBtn) {
    userManagementBtn.classList.toggle('hidden', !canManageUsers);
  }
}

function resetAuthForm() {
  usernameInput.value = '';
  passwordInput.value = '';
  usernameInput.setAttribute('aria-invalid', 'false');
  passwordInput.setAttribute('aria-invalid', 'false');
  loginError.textContent = '';
}

function setAuthMode(mode) {
  authMode = mode;

  if (resetTokenFromUrl) {
    authTitle.textContent = 'Set your Book Wheel password';
    authMessage.textContent = 'Use the secure reset link from your administrator.';
    loginForm.classList.add('hidden');
    resetPasswordForm.classList.remove('hidden');
    return;
  }

  loginForm.classList.remove('hidden');
  resetPasswordForm.classList.add('hidden');

  if (mode === 'setup') {
    authTitle.textContent = 'Create your Book Wheel account';
    authMessage.textContent = 'No account exists yet. Create one to begin.';
    authSubmitBtn.textContent = 'Create account';
    return;
  }

  authTitle.textContent = 'Book Wheel Login';
  authMessage.textContent = 'Log in with your existing account.';
  authSubmitBtn.textContent = 'Log in';
}

function openResetLinkDialog(result) {
  resetLinkError.textContent = '';
  const expiresAt = result.expiresAtUtc ? new Date(result.expiresAtUtc) : null;
  const expiryText = expiresAt && !Number.isNaN(expiresAt.getTime())
    ? `Link expires ${expiresAt.toLocaleString()}.`
    : 'Link expires in 24 hours.';

  resetLinkMessage.textContent = `Reset link created for ${result.username}. ${expiryText}`;
  resetLinkValue.value = result.resetLink || '';

  openDialog(resetLinkDialog, resetLinkValue);
}

function closeResetLinkDialog() {
  resetLinkError.textContent = '';
  resetLinkValue.value = '';

  closeDialog(resetLinkDialog);
}

async function requestJson(url, options = {}) {
  const response = await fetch(url, {
    headers: {
      'Content-Type': 'application/json',
      ...(options.headers || {})
    },
    credentials: 'same-origin',
    ...options
  });

  const contentType = response.headers.get('content-type') || '';
  const payload = contentType.includes('application/json') ? await response.json() : null;

  if (!response.ok) {
    throw new Error(payload?.message || 'Request failed.');
  }

  return payload;
}

function renderAppVersion(version) {
  if (!appVersionEl) {
    return;
  }

  appVersionEl.textContent = `Version: ${version || 'unknown'}`;
}

async function loadAppVersion() {
  try {
    const versionInfo = await requestJson('/api/version');
    renderAppVersion(versionInfo?.version);
  } catch {
    renderAppVersion('unknown');
  }
}

function resetUserManagementMessages() {
  userManagementError.textContent = '';
  userManagementMessage.textContent = '';
}

function renderUserCount(filteredCount, totalCount) {
  if (!userCountBadge) {
    return;
  }

  if (totalCount === 0) {
    userCountBadge.textContent = '0 users';
    return;
  }

  userCountBadge.textContent = filteredCount === totalCount
    ? `${totalCount} users`
    : `${filteredCount} of ${totalCount} users`;
}

function closeUserManagementDialog() {
  resetUserManagementMessages();
  allUsers = [];
  pendingDeleteUser = null;
  if (userSearchInput) {
    userSearchInput.value = '';
  }

  closeDialog(userManagementDialog);
}

function closeDeleteUserDialog() {
  pendingDeleteUser = null;
  deleteUserError.textContent = '';
  confirmDeleteUserBtn.disabled = false;
  cancelDeleteUserBtn.disabled = false;

  closeDialog(deleteUserDialog);
}

function openDeleteUserDialog(user) {
  pendingDeleteUser = user;
  deleteUserError.textContent = '';
  deleteUserConfirmMessage.textContent = `Remove user "${user.username}" and all of their books?`;

  openDialog(deleteUserDialog, confirmDeleteUserBtn);
}

async function confirmDeleteUser() {
  if (!pendingDeleteUser) {
    return;
  }

  confirmDeleteUserBtn.disabled = true;
  cancelDeleteUserBtn.disabled = true;

  const result = await requestJson(`/api/users/${pendingDeleteUser.userId}`, {
    method: 'DELETE'
  });

  closeDeleteUserDialog();
  userManagementMessage.textContent = `Removed user ${result.username}. Deleted ${result.removedBooks} books.`;
  showToast(`Removed user ${result.username}.`, 'success');
  await loadUsers();
}

function renderUserRows(users) {
  userList.innerHTML = '';

  const filterTerm = (userSearchInput?.value || '').trim().toLocaleLowerCase();
  const visibleUsers = filterTerm
    ? users.filter(user => user.username.toLocaleLowerCase().includes(filterTerm))
    : users;

  renderUserCount(visibleUsers.length, users.length);

  if (!visibleUsers.length) {
    userList.innerHTML = '<div class="user-list-empty"><span class="message">No users match this filter.</span></div>';
    return;
  }

  visibleUsers.forEach(user => {
    const row = document.createElement('div');
    row.className = 'user-row';

    const header = document.createElement('div');
    header.className = 'user-row-header';
    const nameLine = document.createElement('div');
    nameLine.className = 'user-name-line';
    const nameText = document.createElement('span');
    nameText.className = 'user-name-text';
    const metaLine = document.createElement('div');
    metaLine.className = 'user-meta-line';
    const rolePill = document.createElement('span');
    rolePill.className = 'user-role-pill';
    rolePill.textContent = user.isAdmin ? 'Administrator' : 'Standard user';

    const createdDate = user.createdAtUtc ? new Date(user.createdAtUtc) : null;
    metaLine.textContent = createdDate && !Number.isNaN(createdDate.getTime())
      ? `Created ${createdDate.toLocaleDateString()}`
      : 'Created date unavailable';

    const username = document.createElement('input');
    username.className = 'user-input';
    username.value = user.username;
    username.maxLength = 64;

    const usernameLabel = document.createElement('label');
    usernameLabel.textContent = 'Username';
    usernameLabel.appendChild(username);

    const adminLabel = document.createElement('label');
    adminLabel.className = 'checkbox-row';
    const adminCheckbox = document.createElement('input');
    adminCheckbox.type = 'checkbox';
    adminCheckbox.checked = Boolean(user.isAdmin);
    const adminText = document.createElement('span');
    adminText.textContent = 'Admin';
    adminLabel.append(adminCheckbox, adminText);

    const disabledLabel = document.createElement('label');
    disabledLabel.className = 'checkbox-row';
    const disabledCheckbox = document.createElement('input');
    disabledCheckbox.type = 'checkbox';
    disabledCheckbox.checked = Boolean(user.isDisabled);
    const disabledText = document.createElement('span');
    disabledText.textContent = 'Disabled';
    disabledLabel.append(disabledCheckbox, disabledText);

    const forceResetLabel = document.createElement('label');
    forceResetLabel.className = 'checkbox-row';
    const forceResetCheckbox = document.createElement('input');
    forceResetCheckbox.type = 'checkbox';
    forceResetCheckbox.checked = Boolean(user.forcePasswordReset);
    const forceResetText = document.createElement('span');
    forceResetText.textContent = 'Require reset';
    forceResetLabel.append(forceResetCheckbox, forceResetText);

    const lockLabel = document.createElement('label');
    lockLabel.className = 'checkbox-row';
    const lockCheckbox = document.createElement('input');
    lockCheckbox.type = 'checkbox';
    lockCheckbox.checked = Boolean(user.isLocked);
    const lockText = document.createElement('span');
    lockText.textContent = 'Locked';
    lockLabel.append(lockCheckbox, lockText);

    const saveButton = document.createElement('button');
    saveButton.type = 'button';
    saveButton.textContent = 'Save';
    saveButton.className = 'secondary';
    saveButton.setAttribute('aria-label', `Save changes for ${user.username}`);

    const deleteButton = document.createElement('button');
    deleteButton.type = 'button';
    deleteButton.textContent = 'Remove';
    deleteButton.className = 'user-delete-btn';
    deleteButton.setAttribute('aria-label', `Remove user ${user.username}`);

    const resetLinkButton = document.createElement('button');
    resetLinkButton.type = 'button';
    resetLinkButton.textContent = 'Generate reset link';
    resetLinkButton.className = 'secondary';
    resetLinkButton.setAttribute('aria-label', `Generate password reset link for ${user.username}`);

    const actions = document.createElement('div');
    actions.className = 'user-row-actions';
    actions.append(saveButton, resetLinkButton, deleteButton);

    const editGrid = document.createElement('div');
    editGrid.className = 'user-edit-grid';
    editGrid.append(usernameLabel, adminLabel, disabledLabel, forceResetLabel, lockLabel);

    const isCurrentUser = currentUser && user.userId === currentUser.userId;
    const firstUserId = users[0]?.userId || null;
    const isFirstUser = firstUserId === user.userId;
    let locked = false;

    const evaluateDirty = () => {
      if (isCurrentUser || locked) {
        saveButton.disabled = true;
        deleteButton.disabled = true;
        return;
      }

      const hasChanges =
        username.value.trim() !== user.username ||
        adminCheckbox.checked !== Boolean(user.isAdmin) ||
        disabledCheckbox.checked !== Boolean(user.isDisabled) ||
        forceResetCheckbox.checked !== Boolean(user.forcePasswordReset) ||
        lockCheckbox.checked !== Boolean(user.isLocked);

      saveButton.disabled = !hasChanges;
      resetLinkButton.disabled = false;
      deleteButton.disabled = false;
    };

    if (isCurrentUser || isFirstUser) {
      row.classList.add('user-row-disabled');
      username.disabled = true;
      adminCheckbox.disabled = true;
      disabledCheckbox.disabled = true;
      forceResetCheckbox.disabled = true;
      lockCheckbox.disabled = true;
      saveButton.disabled = true;
      resetLinkButton.disabled = true;
      deleteButton.disabled = true;

      if (isCurrentUser) {
        saveButton.title = 'Use account settings for your own account updates.';
        resetLinkButton.title = 'You cannot generate a reset link for the active account.';
        deleteButton.title = 'You cannot remove the active account.';
      }

      if (isFirstUser) {
        deleteButton.title = 'The first account cannot be removed.';
      }
    } else {
      saveButton.disabled = true;
      resetLinkButton.disabled = false;
      deleteButton.disabled = false;
    }

    const togglePendingState = pending => {
      locked = pending;
      username.disabled = pending;
      adminCheckbox.disabled = pending;
      disabledCheckbox.disabled = pending;
      forceResetCheckbox.disabled = pending;
      lockCheckbox.disabled = pending;
      resetLinkButton.disabled = pending;
      deleteButton.disabled = pending;
      saveButton.textContent = pending ? 'Saving...' : 'Save';
      evaluateDirty();
    };

    username.addEventListener('input', evaluateDirty);
    adminCheckbox.addEventListener('change', evaluateDirty);
    disabledCheckbox.addEventListener('change', evaluateDirty);
    forceResetCheckbox.addEventListener('change', evaluateDirty);
    lockCheckbox.addEventListener('change', evaluateDirty);

    saveButton.addEventListener('click', async () => {
      userManagementError.textContent = '';
      userManagementMessage.textContent = '';

      const trimmedUsername = username.value.trim();
      if (!trimmedUsername) {
        userManagementError.textContent = 'Username is required.';
        return;
      }

      togglePendingState(true);
      try {
        await requestJson(`/api/users/${user.userId}`, {
          method: 'PUT',
          body: JSON.stringify({
            username: trimmedUsername,
            isAdmin: adminCheckbox.checked,
            isDisabled: disabledCheckbox.checked,
            forcePasswordReset: forceResetCheckbox.checked,
            isLocked: lockCheckbox.checked
          })
        });
        userManagementMessage.textContent = `Updated user ${trimmedUsername}.`;
        showToast(`Updated user ${trimmedUsername}.`, 'success');
        await loadUsers();
      } catch (error) {
        userManagementError.textContent = error.message;
        showToast(error.message, 'error');
      } finally {
        togglePendingState(false);
      }
    });

    resetLinkButton.addEventListener('click', async () => {
      userManagementError.textContent = '';
      userManagementMessage.textContent = '';

      resetLinkButton.disabled = true;
      const originalLabel = resetLinkButton.textContent;
      resetLinkButton.textContent = 'Generating...';
      try {
        const result = await requestJson(`/api/users/${user.userId}/password-reset-link`, {
          method: 'POST'
        });
        openResetLinkDialog(result);
        userManagementMessage.textContent = `Generated a secure reset link for ${result.username}.`;
        showToast(`Reset link generated for ${result.username}.`, 'success');
      } catch (error) {
        userManagementError.textContent = error.message;
        showToast(error.message, 'error');
      } finally {
        resetLinkButton.textContent = originalLabel;
        resetLinkButton.disabled = false;
      }
    });

    deleteButton.addEventListener('click', () => {
      openDeleteUserDialog(user);
    });

    nameText.textContent = isCurrentUser ? `${user.username} (you)` : user.username;
    nameLine.append(nameText, rolePill);
    header.append(nameLine, metaLine);

    row.append(header, editGrid, actions);
    userList.appendChild(row);
  });
}

async function loadUsers() {
  const payload = await requestJson('/api/users');
  allUsers = payload.users || [];
  renderUserRows(allUsers);
}

async function openUserManagementDialog() {
  resetUserManagementMessages();
  await loadUsers();

  openDialog(userManagementDialog, createUserUsername);
}

function drawWheel() {
  const size = canvas.width;
  const radius = size / 2;
  ctx.clearRect(0, 0, size, size);
  const computedStyles = getComputedStyle(document.documentElement);
  const wheelTextColor = computedStyles.getPropertyValue('--input-bg').trim() || '#0b1220';
  const emptyStateColor = computedStyles.getPropertyValue('--muted').trim() || '#94a3b8';

  if (!wheelBooks.length) {
    ctx.save();
    ctx.fillStyle = emptyStateColor;
    ctx.font = '20px Arial';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText('Add books to spin', radius, radius);
    ctx.restore();
    return;
  }

  const step = (Math.PI * 2) / wheelBooks.length;
  wheelBooks.forEach((book, index) => {
    const start = index * step - Math.PI / 2;
    const end = start + step;

    ctx.beginPath();
    ctx.moveTo(radius, radius);
    ctx.arc(radius, radius, radius - 12, start, end);
    ctx.closePath();
    ctx.fillStyle = ['#38bdf8', '#60a5fa', '#818cf8', '#f472b6', '#34d399', '#fbbf24'][index % 6];
    ctx.fill();

    ctx.save();
    ctx.translate(radius, radius);
    ctx.rotate(start + step / 2);
    ctx.fillStyle = wheelTextColor;
    ctx.font = 'bold 18px Arial';
    ctx.textAlign = 'right';
    ctx.textBaseline = 'middle';
    ctx.fillText(book.title.length > 18 ? `${book.title.slice(0, 18)}...` : book.title, radius - 24, 0);
    ctx.restore();
  });
}

function renderActiveBooks() {
  activeBooksEl.innerHTML = '';
  if (!activeBooks.length) {
    activeBooksEl.innerHTML = `
      <div class="books-empty-state" role="status" aria-live="polite">
        <h3>No books yet</h3>
        <p class="message">Add your first title above to populate the wheel.</p>
        <p class="message">Tip: after adding books, press the Spin button or hit Enter on the wheel.</p>
      </div>`;
    selectedBookEl.textContent = 'Add your first book to begin spinning.';
    renderBookCount();
    renderPagination();
    return;
  }

  clampCurrentPage();
  const start = (currentPage - 1) * BOOKS_PER_PAGE;
  const booksToShow = activeBooks.slice(start, start + BOOKS_PER_PAGE);

  booksToShow.forEach(book => {
    const row = document.createElement('div');
    row.className = 'book-row';

    const titleButton = document.createElement('button');
    titleButton.type = 'button';
    titleButton.className = 'book-title-btn';
    titleButton.textContent = book.title;
    titleButton.title = 'Edit this title';
    titleButton.setAttribute('aria-label', `Edit book title: ${book.title}`);
    titleButton.addEventListener('click', () => editBook(book));

    const removeButton = document.createElement('button');
    removeButton.type = 'button';
    removeButton.className = 'book-remove-btn';
    removeButton.textContent = 'Remove';
    removeButton.title = 'Remove from active list';
    removeButton.setAttribute('aria-label', `Remove book: ${book.title}`);
    removeButton.addEventListener('click', () => removeBook(book));

    const actions = document.createElement('div');
    actions.className = 'book-row-actions';
    actions.appendChild(removeButton);

    row.append(titleButton, actions);
    activeBooksEl.appendChild(row);
  });

  renderBookCount();
  renderPagination();
}

async function refreshBooks(options = {}) {
  const data = await requestJson('/api/books');
  activeBooks = data.activeBooks || data.books || [];
  setWheelBooksFromActive(Boolean(options.shuffleWheel));
  if (options.goToLastPage) {
    currentPage = getTotalPages();
  } else {
    clampCurrentPage();
  }
  drawWheel();
  renderActiveBooks();
  spinBtn.disabled = activeBooks.length === 0 || spinning;
}

async function editBook(book) {
  editError.textContent = '';
  editBookId.value = book.id;
  editBookTitle.value = book.title;
  openDialog(editDialog, editBookTitle);
}

async function saveEdit() {
  const trimmed = editBookTitle.value.trim();
  if (!trimmed) {
    editBookTitle.setAttribute('aria-invalid', 'true');
    editError.textContent = 'Title cannot be empty.';
    return;
  }

  editBookTitle.setAttribute('aria-invalid', 'false');

  await requestJson(`/api/books/${editBookId.value}`, {
    method: 'PUT',
    body: JSON.stringify({ title: trimmed })
  });

  closeDialog(editDialog);
  bookMessage.textContent = 'Book updated.';
  showToast('Book title updated.', 'success');
  await refreshBooks();
}

async function removeBook(book) {
  pendingDeleteBook = book;
  deleteError.textContent = '';
  deleteConfirmMessage.textContent = `Remove "${book.title}" from the active list?`;
  openDialog(deleteDialog, confirmDeleteBtn);
}

function closeDeleteDialog() {
  pendingDeleteBook = null;
  deleteError.textContent = '';
  confirmDeleteBtn.disabled = false;
  cancelDeleteBtn.disabled = false;

  closeDialog(deleteDialog);
}

async function confirmDelete() {
  if (!pendingDeleteBook) {
    return;
  }

  confirmDeleteBtn.disabled = true;
  cancelDeleteBtn.disabled = true;

  await requestJson(`/api/books/${pendingDeleteBook.id}`, {
    method: 'DELETE'
  });

  closeDeleteDialog();
  bookMessage.textContent = 'Book removed from the active list.';
  showToast('Book removed.', 'success');
  await refreshBooks();
}

function setTransferTab(tabName) {
  const showImport = tabName === 'import';
  importPanel.classList.toggle('hidden', !showImport);
  exportPanel.classList.toggle('hidden', showImport);
  importPanel.setAttribute('aria-hidden', showImport ? 'false' : 'true');
  exportPanel.setAttribute('aria-hidden', showImport ? 'true' : 'false');
  importTabBtn.classList.toggle('active', showImport);
  exportTabBtn.classList.toggle('active', !showImport);
  importTabBtn.setAttribute('aria-selected', showImport ? 'true' : 'false');
  exportTabBtn.setAttribute('aria-selected', showImport ? 'false' : 'true');
  importTabBtn.tabIndex = showImport ? 0 : -1;
  exportTabBtn.tabIndex = showImport ? -1 : 0;
}

function moveTransferTabFocus(direction) {
  const tabs = [importTabBtn, exportTabBtn];
  const currentIndex = tabs.findIndex(tab => tab === document.activeElement);
  const nextIndex = currentIndex < 0
    ? 0
    : (currentIndex + direction + tabs.length) % tabs.length;
  const nextTab = tabs[nextIndex];
  setTransferTab(nextTab === importTabBtn ? 'import' : 'export');
  nextTab.focus();
}

function openTransferDialog() {
  transferMessage.textContent = '';
  transferError.textContent = '';
  if (importJsonFile) {
    importJsonFile.value = '';
  }
  setTransferTab('import');

  openDialog(transferDialog, importTabBtn);
}

function closeTransferDialog() {
  transferMessage.textContent = '';
  transferError.textContent = '';

  closeDialog(transferDialog);
}

function parseImportTitles(rawJson) {
  const parsed = JSON.parse(rawJson);
  const source = Array.isArray(parsed)
    ? parsed
    : Array.isArray(parsed?.books)
      ? parsed.books
      : null;

  if (!source) {
    throw new Error('Invalid JSON format. Use an array or an object with a books array.');
  }

  const titles = [];
  source.forEach(item => {
    let title = '';
    if (typeof item === 'string') {
      title = item;
    } else if (item && typeof item.title === 'string') {
      title = item.title;
    }

    const trimmed = title.trim();
    if (trimmed) {
      titles.push(trimmed);
    }
  });

  return titles;
}

async function importBooksFromJsonFile() {
  transferMessage.textContent = '';
  transferError.textContent = '';

  const importFile = importJsonFile.files?.[0] || null;
  if (!importFile) {
    importJsonFile.setAttribute('aria-invalid', 'true');
    transferError.textContent = 'Choose a JSON file to import.';
    return;
  }

  importJsonFile.setAttribute('aria-invalid', 'false');

  const rawJson = (await importFile.text()).trim();
  if (!rawJson) {
    transferError.textContent = 'The selected file is empty.';
    return;
  }

  const importTitles = parseImportTitles(rawJson);
  if (!importTitles.length) {
    transferError.textContent = 'No valid titles found in JSON.';
    return;
  }

  const existingTitles = new Set(activeBooks.map(book => normalizeTitle(book.title)));
  const seenImportTitles = new Set();
  const titlesToAdd = [];
  let skippedMatches = 0;

  importTitles.forEach(title => {
    const normalized = normalizeTitle(title);
    if (seenImportTitles.has(normalized) || existingTitles.has(normalized)) {
      skippedMatches += 1;
      return;
    }

    seenImportTitles.add(normalized);
    titlesToAdd.push(title);
  });

  let addedCount = 0;
  for (const title of titlesToAdd) {
    await requestJson('/api/books', {
      method: 'POST',
      body: JSON.stringify({ title })
    });
    addedCount += 1;
  }

  if (addedCount > 0) {
    await refreshBooks({ goToLastPage: true, shuffleWheel: true });
  }

  transferMessage.textContent = `Import complete. Added ${addedCount}, skipped ${skippedMatches} matches.`;
}

function downloadExportJsonFile() {
  const exportPayload = {
    books: activeBooks.map(book => ({ title: book.title }))
  };

  const payload = JSON.stringify(exportPayload, null, 2);
  const blob = new Blob([payload], { type: 'application/json' });
  const downloadUrl = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  const timestamp = new Date().toISOString().replace(/[\:\.]/g, '-');
  anchor.href = downloadUrl;
  anchor.download = `bookwheel-export-${timestamp}.json`;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(downloadUrl);

  transferMessage.textContent = 'Export file download started.';
}

editForm.addEventListener('submit', async event => {
  event.preventDefault();
  editError.textContent = '';

  try {
    await saveEdit();
  } catch (error) {
    editError.textContent = error.message;
  }
});

cancelEditBtn.addEventListener('click', () => {
  closeDialog(editDialog);
});

cancelDeleteBtn.addEventListener('click', () => {
  closeDeleteDialog();
});

cancelDeleteUserBtn.addEventListener('click', () => {
  closeDeleteUserDialog();
});

confirmDeleteUserBtn.addEventListener('click', async () => {
  try {
    await confirmDeleteUser();
  } catch (error) {
    deleteUserError.textContent = error.message;
    confirmDeleteUserBtn.disabled = false;
    cancelDeleteUserBtn.disabled = false;
  }
});

confirmDeleteBtn.addEventListener('click', async () => {
  try {
    await confirmDelete();
  } catch (error) {
    deleteError.textContent = error.message;
    confirmDeleteBtn.disabled = false;
    cancelDeleteBtn.disabled = false;
  }
});

importTabBtn.addEventListener('click', () => {
  transferError.textContent = '';
  transferMessage.textContent = '';
  setTransferTab('import');
});

exportTabBtn.addEventListener('click', () => {
  transferError.textContent = '';
  transferMessage.textContent = '';
  setTransferTab('export');
});

function handleTransferTabKeydown(event) {
  if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') {
    event.preventDefault();
    moveTransferTabFocus(-1);
    return;
  }

  if (event.key === 'ArrowRight' || event.key === 'ArrowDown') {
    event.preventDefault();
    moveTransferTabFocus(1);
    return;
  }

  if (event.key === 'Home') {
    event.preventDefault();
    setTransferTab('import');
    importTabBtn.focus();
    return;
  }

  if (event.key === 'End') {
    event.preventDefault();
    setTransferTab('export');
    exportTabBtn.focus();
  }
}

importTabBtn.addEventListener('keydown', handleTransferTabKeydown);
exportTabBtn.addEventListener('keydown', handleTransferTabKeydown);

importFileBtn.addEventListener('click', async () => {
  importFileBtn.disabled = true;
  try {
    await importBooksFromJsonFile();
  } catch (error) {
    transferError.textContent = error.message;
  } finally {
    importFileBtn.disabled = false;
  }
});

if (importJsonFile) {
  importJsonFile.addEventListener('change', () => {
    importJsonFile.setAttribute('aria-invalid', 'false');
  });
}

downloadExportBtn.addEventListener('click', () => {
  transferError.textContent = '';
  transferMessage.textContent = '';
  downloadExportJsonFile();
});

cancelTransferBtn.addEventListener('click', () => {
  closeTransferDialog();
});

closeExportBtn.addEventListener('click', () => {
  closeTransferDialog();
});

loginForm.addEventListener('submit', async event => {
  event.preventDefault();
  loginError.textContent = '';
  const originalButtonText = authSubmitBtn.textContent;
  const username = usernameInput.value.trim();
  const password = passwordInput.value;

  if (!username || !password) {
    usernameInput.setAttribute('aria-invalid', 'true');
    passwordInput.setAttribute('aria-invalid', 'true');
    loginError.textContent = 'Username and password are required.';
    return;
  }

  usernameInput.setAttribute('aria-invalid', 'false');
  passwordInput.setAttribute('aria-invalid', 'false');

  authSubmitBtn.disabled = true;
  authSubmitBtn.textContent = authMode === 'setup' ? 'Creating account...' : 'Logging in...';
  usernameInput.disabled = true;
  passwordInput.disabled = true;

  try {
    const authResult = await requestJson(authMode === 'setup' ? '/api/auth/setup' : '/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({
        username,
        password
      })
    });
    applyCurrentUser(authResult.user || null);
    showApp(true);
    showToast(authMode === 'setup' ? 'Account created and signed in.' : 'Signed in successfully.', 'success');
    await refreshBooks();
    setAuthMode('login');
  } catch (error) {
    loginError.textContent = error.message === 'Failed to fetch'
      ? 'Cannot connect to the server. Make sure the app is running, then try again.'
      : error.message;
    showToast(loginError.textContent, 'error');
  } finally {
    authSubmitBtn.disabled = false;
    authSubmitBtn.textContent = originalButtonText;
    usernameInput.disabled = false;
    passwordInput.disabled = false;
  }
});

bookForm.addEventListener('submit', async event => {
  event.preventDefault();
  bookMessage.textContent = '';
  const trimmedTitle = bookTitle.value.trim();

  if (!trimmedTitle) {
    bookTitle.setAttribute('aria-invalid', 'true');
    bookMessage.textContent = 'Book title is required.';
    return;
  }

  bookTitle.setAttribute('aria-invalid', 'false');

  try {
    bookTitle.disabled = true;
    await requestJson('/api/books', {
      method: 'POST',
      body: JSON.stringify({ title: trimmedTitle })
    });
    bookTitle.value = '';
    bookMessage.textContent = 'Book added.';
    showToast('Book added to active list.', 'success');
    await refreshBooks({ goToLastPage: true, shuffleWheel: true });
  } catch (error) {
    bookMessage.textContent = error.message;
    showToast(error.message, 'error');
  } finally {
    bookTitle.disabled = false;
  }
});

booksPrevPageBtn.addEventListener('click', () => {
  if (currentPage <= 1) {
    return;
  }

  currentPage -= 1;
  renderActiveBooks();
});

booksNextPageBtn.addEventListener('click', () => {
  if (currentPage >= getTotalPages()) {
    return;
  }

  currentPage += 1;
  renderActiveBooks();
});

spinBtn.addEventListener('click', async () => {
  if (spinning || !activeBooks.length) {
    return;
  }

  spinning = true;
  spinBtn.disabled = true;
  spinBtn.textContent = 'Spinning...';
  selectedBookEl.textContent = 'Spinning...';

  try {
    const result = await requestJson('/api/books/spin', { method: 'POST' });
    const selected = result.selected;

    if (!wheelBooks.length) {
      throw new Error('Add books to spin.');
    }

    const selectedIndex = wheelBooks.findIndex(book => book.id === selected.id);

    if (selectedIndex < 0) {
      throw new Error('Selected book was not found on the current wheel.');
    }

    const slice = 360 / wheelBooks.length;
    const targetAngle = 360 - ((selectedIndex * slice) + slice / 2);
    const normalizedRotation = ((currentRotation % 360) + 360) % 360;
    const fullSpins = isReducedMotionEnabled() ? 1 : 5;
    const rotationDelta = 360 * fullSpins + targetAngle - normalizedRotation;
    const spinDelayMs = isReducedMotionEnabled() ? 120 : 4200;
    currentRotation += rotationDelta;
    canvas.style.transform = `rotate(${currentRotation}deg)`;

    setTimeout(async () => {
      activeBooks = result.activeBooks || [];
      clampCurrentPage();
      drawWheel();
      renderActiveBooks();
      selectedBookEl.textContent = `Last selected: ${selected.title}`;
      spinning = false;
      spinBtn.disabled = activeBooks.length === 0;
      spinBtn.textContent = 'Spin';
      showToast(`Selected: ${selected.title}`, 'success');
    }, spinDelayMs);
  } catch (error) {
    spinning = false;
    selectedBookEl.textContent = error.message;
    spinBtn.textContent = 'Spin';
    showToast(error.message, 'error');
    await refreshBooks();
  }
});

if (canvas) {
  canvas.addEventListener('keydown', event => {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      spinBtn.click();
    }
  });
}

document.addEventListener('keydown', event => {
  if (appView.classList.contains('hidden') || !shouldHandlePaginationHotkey(event)) {
    return;
  }

  if (event.key === 'ArrowLeft' && !booksPrevPageBtn.disabled) {
    booksPrevPageBtn.click();
  }

  if (event.key === 'ArrowRight' && !booksNextPageBtn.disabled) {
    booksNextPageBtn.click();
  }
});

logoutBtn.addEventListener('click', async () => {
  await requestJson('/api/auth/logout', { method: 'POST' });
  currentPage = 1;
  applyCurrentUser(null);
  resetAuthForm();
  setAuthMode('login');
  showApp(false);
  showToast('Signed out.', 'info');
});

if (userManagementBtn) {
  userManagementBtn.addEventListener('click', async () => {
    try {
      await openUserManagementDialog();
    } catch (error) {
      userManagementError.textContent = error.message;
    }
  });
}

if (closeUserManagementBtn) {
  closeUserManagementBtn.addEventListener('click', () => {
    closeUserManagementDialog();
  });
}

if (createUserForm) {
  createUserForm.addEventListener('submit', async event => {
    event.preventDefault();
    resetUserManagementMessages();

    const username = createUserUsername.value.trim();

    if (!username) {
      createUserUsername.setAttribute('aria-invalid', 'true');
      userManagementError.textContent = 'Username is required.';
      return;
    }

    createUserUsername.setAttribute('aria-invalid', 'false');

    const createUserSubmitButton = createUserForm.querySelector('button[type="submit"]');
    setButtonBusy(createUserSubmitButton, true, 'Creating...', 'Create user');
    createUserUsername.disabled = true;
    createUserIsAdmin.disabled = true;

    try {
      const result = await requestJson('/api/users', {
        method: 'POST',
        body: JSON.stringify({
          username,
          isAdmin: createUserIsAdmin.checked
        })
      });

      userManagementMessage.textContent = `Created user ${username}.`;
      showToast(`Created user ${username}.`, 'success');
      openResetLinkDialog({
        username: result.username,
        resetLink: result.setupLink,
        expiresAtUtc: result.setupLinkExpiresAtUtc
      });
      createUserForm.reset();
      await loadUsers();
    } catch (error) {
      userManagementError.textContent = error.message;
      showToast(error.message, 'error');
    } finally {
      setButtonBusy(createUserSubmitButton, false, 'Creating...', 'Create user');
      createUserUsername.disabled = false;
      createUserIsAdmin.disabled = false;
    }
  });
}

if (copyResetLinkBtn) {
  copyResetLinkBtn.addEventListener('click', async () => {
    resetLinkError.textContent = '';
    try {
      if (!resetLinkValue.value) {
        throw new Error('No reset link is available.');
      }

      await navigator.clipboard.writeText(resetLinkValue.value);
      resetLinkMessage.textContent = 'Reset link copied to clipboard.';
    } catch {
      resetLinkError.textContent = 'Copy failed. Select and copy the link manually.';
      resetLinkValue.select();
    }
  });
}

if (closeResetLinkBtn) {
  closeResetLinkBtn.addEventListener('click', () => {
    closeResetLinkDialog();
  });
}

if (resetPasswordForm) {
  resetPasswordForm.addEventListener('submit', async event => {
    event.preventDefault();
    resetPasswordError.textContent = '';
    resetPasswordMessage.textContent = '';

    if (!resetTokenFromUrl) {
      resetPasswordError.textContent = 'Reset token is missing.';
      return;
    }

    const newPassword = resetPassword.value;
    const confirmPassword = resetPasswordConfirm.value;

    if (!newPassword || !confirmPassword) {
      resetPassword.setAttribute('aria-invalid', 'true');
      resetPasswordConfirm.setAttribute('aria-invalid', 'true');
      resetPasswordError.textContent = 'Both password fields are required.';
      return;
    }

    if (newPassword.length < 8) {
      resetPassword.setAttribute('aria-invalid', 'true');
      resetPasswordConfirm.setAttribute('aria-invalid', 'true');
      resetPasswordError.textContent = 'Password must be at least 8 characters.';
      return;
    }

    if (newPassword !== confirmPassword) {
      resetPassword.setAttribute('aria-invalid', 'true');
      resetPasswordConfirm.setAttribute('aria-invalid', 'true');
      resetPasswordError.textContent = 'Passwords do not match.';
      return;
    }

    resetPassword.setAttribute('aria-invalid', 'false');
    resetPasswordConfirm.setAttribute('aria-invalid', 'false');

    resetPasswordSubmitBtn.disabled = true;
    const originalText = resetPasswordSubmitBtn.textContent;
    resetPasswordSubmitBtn.textContent = 'Saving...';

    try {
      await requestJson('/api/auth/password-reset/complete', {
        method: 'POST',
        body: JSON.stringify({
          token: resetTokenFromUrl,
          newPassword
        })
      });

      resetPasswordMessage.textContent = 'Password updated. You can now log in with your new password.';
      showToast('Password updated successfully.', 'success');
      resetTokenFromUrl = null;
      resetPasswordForm.reset();
      const url = new URL(window.location.href);
      url.searchParams.delete('resetToken');
      window.history.replaceState({}, document.title, url.pathname + url.search);
      setAuthMode('login');
    } catch (error) {
      resetPasswordError.textContent = error.message;
      showToast(error.message, 'error');
    } finally {
      resetPasswordSubmitBtn.disabled = false;
      resetPasswordSubmitBtn.textContent = originalText;
    }
  });
}

if (userSearchInput) {
  userSearchInput.addEventListener('input', () => {
    renderUserRows(allUsers);
  });
}

if (themeToggleBtn) {
  themeToggleBtn.addEventListener('click', toggleTheme);
}

if (importExportBtn) {
  importExportBtn.addEventListener('click', openTransferDialog);
}

(async () => {
  const query = new URLSearchParams(window.location.search);
  resetTokenFromUrl = query.get('resetToken');

  if (resetTokenFromUrl) {
    showApp(false);
    loginForm.classList.add('hidden');
    resetPasswordForm.classList.remove('hidden');
    authTitle.textContent = 'Validate password reset link';
    authMessage.textContent = 'Checking your secure reset link...';

    try {
      const validation = await requestJson('/api/auth/password-reset/validate', {
        method: 'POST',
        body: JSON.stringify({ token: resetTokenFromUrl })
      });

      const expiresAt = validation.expiresAtUtc ? new Date(validation.expiresAtUtc) : null;
      const expiryText = expiresAt && !Number.isNaN(expiresAt.getTime())
        ? `This link expires at ${expiresAt.toLocaleString()}.`
        : 'This link expires in 24 hours.';

      authTitle.textContent = `Set password for ${validation.username || 'your account'}`;
      authMessage.textContent = expiryText;
      resetPasswordMessage.textContent = '';
      resetPasswordError.textContent = '';
      showToast('Reset link validated.', 'success');
      resetPassword.focus();
    } catch (error) {
      resetPasswordForm.classList.add('hidden');
      loginForm.classList.remove('hidden');
      resetTokenFromUrl = null;
      const url = new URL(window.location.href);
      url.searchParams.delete('resetToken');
      window.history.replaceState({}, document.title, url.pathname + url.search);
      authTitle.textContent = 'Book Wheel Login';
      authMessage.textContent = 'Log in with your existing account.';
      loginError.textContent = error.message || 'The password reset link is invalid or has expired.';
      showToast(loginError.textContent, 'error');
    }
  }

  applyTheme(getPreferredTheme());
  await loadAppVersion();

  try {
    const status = await requestJson('/api/auth/status');
    setAuthMode(status.setupRequired ? 'setup' : 'login');
    const me = await requestJson('/api/auth/me');
    applyCurrentUser({
      userId: me.userId,
      username: me.username,
      isAdmin: me.isAdmin
    });
    showApp(true);
    await refreshBooks();
  } catch {
    applyCurrentUser(null);
    showApp(false);
    if (authMode !== 'setup') {
      setAuthMode('login');
    }
    drawWheel();
  }
})();
