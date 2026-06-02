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
const spinBtn = document.getElementById('spinBtn');
const logoutBtn = document.getElementById('logoutBtn');
const userManagementBtn = document.getElementById('userManagementBtn');
const importExportBtn = document.getElementById('importExportBtn');
const themeToggleBtn = document.getElementById('themeToggleBtn');
const themeToggleIcon = document.getElementById('themeToggleIcon');
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
const createUserPassword = document.getElementById('createUserPassword');
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
  if (userManagementBtn) {
    userManagementBtn.classList.toggle('hidden', !canManageUsers);
  }
}

function resetAuthForm() {
  usernameInput.value = '';
  passwordInput.value = '';
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

  if (typeof resetLinkDialog.showModal === 'function') {
    resetLinkDialog.showModal();
  } else {
    resetLinkDialog.setAttribute('open', 'open');
  }
}

function closeResetLinkDialog() {
  resetLinkError.textContent = '';
  resetLinkValue.value = '';

  if (typeof resetLinkDialog.close === 'function') {
    resetLinkDialog.close();
  } else {
    resetLinkDialog.removeAttribute('open');
  }
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

  if (typeof userManagementDialog.close === 'function') {
    userManagementDialog.close();
  } else {
    userManagementDialog.removeAttribute('open');
  }
}

function closeDeleteUserDialog() {
  pendingDeleteUser = null;
  deleteUserError.textContent = '';
  confirmDeleteUserBtn.disabled = false;
  cancelDeleteUserBtn.disabled = false;

  if (typeof deleteUserDialog.close === 'function') {
    deleteUserDialog.close();
  } else {
    deleteUserDialog.removeAttribute('open');
  }
}

function openDeleteUserDialog(user) {
  pendingDeleteUser = user;
  deleteUserError.textContent = '';
  deleteUserConfirmMessage.textContent = `Remove user "${user.username}" and all of their books?`;

  if (typeof deleteUserDialog.showModal === 'function') {
    deleteUserDialog.showModal();
  } else {
    deleteUserDialog.setAttribute('open', 'open');
  }
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

    const deleteButton = document.createElement('button');
    deleteButton.type = 'button';
    deleteButton.textContent = 'Remove';
    deleteButton.className = 'user-delete-btn';

    const resetLinkButton = document.createElement('button');
    resetLinkButton.type = 'button';
    resetLinkButton.textContent = 'Generate reset link';
    resetLinkButton.className = 'secondary';

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

  if (typeof userManagementDialog.showModal === 'function') {
    userManagementDialog.showModal();
  } else {
    userManagementDialog.setAttribute('open', 'open');
  }
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
    activeBooksEl.innerHTML = '<span class="message">No active books</span>';
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
    titleButton.addEventListener('click', () => editBook(book));

    const removeButton = document.createElement('button');
    removeButton.type = 'button';
    removeButton.className = 'book-remove-btn';
    removeButton.textContent = 'Remove';
    removeButton.title = 'Remove from active list';
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
  if (typeof editDialog.showModal === 'function') {
    editDialog.showModal();
  } else {
    editDialog.setAttribute('open', 'open');
  }
  editBookTitle.focus();
}

async function saveEdit() {
  const trimmed = editBookTitle.value.trim();
  if (!trimmed) {
    editError.textContent = 'Title cannot be empty.';
    return;
  }

  await requestJson(`/api/books/${editBookId.value}`, {
    method: 'PUT',
    body: JSON.stringify({ title: trimmed })
  });

  editDialog.close();
  bookMessage.textContent = 'Book updated.';
  showToast('Book title updated.', 'success');
  await refreshBooks();
}

async function removeBook(book) {
  pendingDeleteBook = book;
  deleteError.textContent = '';
  deleteConfirmMessage.textContent = `Remove "${book.title}" from the active list?`;
  if (typeof deleteDialog.showModal === 'function') {
    deleteDialog.showModal();
  } else {
    deleteDialog.setAttribute('open', 'open');
  }
}

function closeDeleteDialog() {
  pendingDeleteBook = null;
  deleteError.textContent = '';
  confirmDeleteBtn.disabled = false;
  cancelDeleteBtn.disabled = false;

  if (typeof deleteDialog.close === 'function') {
    deleteDialog.close();
  } else {
    deleteDialog.removeAttribute('open');
  }
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
  importTabBtn.classList.toggle('active', showImport);
  exportTabBtn.classList.toggle('active', !showImport);
  importTabBtn.setAttribute('aria-selected', showImport ? 'true' : 'false');
  exportTabBtn.setAttribute('aria-selected', showImport ? 'false' : 'true');
}

function openTransferDialog() {
  transferMessage.textContent = '';
  transferError.textContent = '';
  if (importJsonFile) {
    importJsonFile.value = '';
  }
  setTransferTab('import');

  if (typeof transferDialog.showModal === 'function') {
    transferDialog.showModal();
  } else {
    transferDialog.setAttribute('open', 'open');
  }
}

function closeTransferDialog() {
  transferMessage.textContent = '';
  transferError.textContent = '';

  if (typeof transferDialog.close === 'function') {
    transferDialog.close();
  } else {
    transferDialog.removeAttribute('open');
  }
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
    transferError.textContent = 'Choose a JSON file to import.';
    return;
  }

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
  editDialog.close();
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
    loginError.textContent = 'Username and password are required.';
    return;
  }

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
    bookMessage.textContent = 'Book title is required.';
    return;
  }

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
    const rotationDelta = 360 * 5 + targetAngle - normalizedRotation;
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
    }, 4200);
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
  const tagName = (event.target && event.target.tagName) ? event.target.tagName.toLowerCase() : '';
  if (tagName === 'input' || tagName === 'textarea' || tagName === 'button') {
    return;
  }

  if (!appView.classList.contains('hidden') && event.key === 'ArrowLeft' && !booksPrevPageBtn.disabled) {
    booksPrevPageBtn.click();
  }

  if (!appView.classList.contains('hidden') && event.key === 'ArrowRight' && !booksNextPageBtn.disabled) {
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
    const password = createUserPassword.value;

    if (!username || !password) {
      userManagementError.textContent = 'Username and password are required.';
      return;
    }

    const createUserSubmitButton = createUserForm.querySelector('button[type="submit"]');
    setButtonBusy(createUserSubmitButton, true, 'Creating...', 'Create user');
    createUserUsername.disabled = true;
    createUserPassword.disabled = true;
    createUserIsAdmin.disabled = true;

    try {
      await requestJson('/api/users', {
        method: 'POST',
        body: JSON.stringify({
          username,
          password,
          isAdmin: createUserIsAdmin.checked
        })
      });

      userManagementMessage.textContent = `Created user ${username}.`;
      showToast(`Created user ${username}.`, 'success');
      createUserForm.reset();
      await loadUsers();
    } catch (error) {
      userManagementError.textContent = error.message;
      showToast(error.message, 'error');
    } finally {
      setButtonBusy(createUserSubmitButton, false, 'Creating...', 'Create user');
      createUserUsername.disabled = false;
      createUserPassword.disabled = false;
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
      resetPasswordError.textContent = 'Both password fields are required.';
      return;
    }

    if (newPassword.length < 8) {
      resetPasswordError.textContent = 'Password must be at least 8 characters.';
      return;
    }

    if (newPassword !== confirmPassword) {
      resetPasswordError.textContent = 'Passwords do not match.';
      return;
    }

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
