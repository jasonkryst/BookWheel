const loginView = document.getElementById('loginView');
const appView = document.getElementById('appView');
const loginForm = document.getElementById('loginForm');
const loginError = document.getElementById('loginError');
const authTitle = document.getElementById('authTitle');
const authMessage = document.getElementById('authMessage');
const authSubmitBtn = document.getElementById('authSubmitBtn');
const usernameInput = document.getElementById('username');
const passwordInput = document.getElementById('password');
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
const themeToggleBtn = document.getElementById('themeToggleBtn');
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

let activeBooks = [];
let spinning = false;
let currentRotation = 0;
let currentPage = 1;
let authMode = 'login';
let pendingDeleteBook = null;
const BOOKS_PER_PAGE = 10;
const THEME_STORAGE_KEY = 'bookwheel-theme';
const DARK_THEME = 'dark';
const LIGHT_THEME = 'light';

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
    themeToggleBtn.textContent = nextThemeLabel;
    themeToggleBtn.setAttribute('aria-label', `Switch to ${nextThemeLabel}`);
    themeToggleBtn.setAttribute('title', `Switch to ${nextThemeLabel}`);
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

function resetAuthForm() {
  usernameInput.value = '';
  passwordInput.value = '';
  loginError.textContent = '';
}

function setAuthMode(mode) {
  authMode = mode;

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

function drawWheel() {
  const size = canvas.width;
  const radius = size / 2;
  ctx.clearRect(0, 0, size, size);
  const computedStyles = getComputedStyle(document.documentElement);
  const wheelTextColor = computedStyles.getPropertyValue('--input-bg').trim() || '#0b1220';
  const emptyStateColor = computedStyles.getPropertyValue('--muted').trim() || '#94a3b8';

  if (!activeBooks.length) {
    ctx.save();
    ctx.fillStyle = emptyStateColor;
    ctx.font = '20px Arial';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText('Add books to spin', radius, radius);
    ctx.restore();
    return;
  }

  const step = (Math.PI * 2) / activeBooks.length;
  activeBooks.forEach((book, index) => {
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
  await refreshBooks();
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

confirmDeleteBtn.addEventListener('click', async () => {
  try {
    await confirmDelete();
  } catch (error) {
    deleteError.textContent = error.message;
    confirmDeleteBtn.disabled = false;
    cancelDeleteBtn.disabled = false;
  }
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

  try {
    await requestJson(authMode === 'setup' ? '/api/auth/setup' : '/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({
        username,
        password
      })
    });
    showApp(true);
    await refreshBooks();
    setAuthMode('login');
  } catch (error) {
    loginError.textContent = error.message === 'Failed to fetch'
      ? 'Cannot connect to the server. Make sure the app is running, then try again.'
      : error.message;
  } finally {
    authSubmitBtn.disabled = false;
    authSubmitBtn.textContent = originalButtonText;
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
    await requestJson('/api/books', {
      method: 'POST',
      body: JSON.stringify({ title: trimmedTitle })
    });
    bookTitle.value = '';
    bookMessage.textContent = 'Book added.';
    await refreshBooks({ goToLastPage: true });
  } catch (error) {
    bookMessage.textContent = error.message;
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
  selectedBookEl.textContent = 'Spinning...';

  const wheelBooks = [...activeBooks];

  try {
    const result = await requestJson('/api/books/spin', { method: 'POST' });
    const selected = result.selected;
    const selectedIndex = wheelBooks.findIndex(book => book.id === selected.id);
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
    }, 4200);
  } catch (error) {
    spinning = false;
    selectedBookEl.textContent = error.message;
    await refreshBooks();
  }
});

logoutBtn.addEventListener('click', async () => {
  await requestJson('/api/auth/logout', { method: 'POST' });
  currentPage = 1;
  resetAuthForm();
  setAuthMode('login');
  showApp(false);
});

if (themeToggleBtn) {
  themeToggleBtn.addEventListener('click', toggleTheme);
}

(async () => {
  applyTheme(getPreferredTheme());

  try {
    const status = await requestJson('/api/auth/status');
    setAuthMode(status.setupRequired ? 'setup' : 'login');
    await requestJson('/api/auth/me');
    showApp(true);
    await refreshBooks();
  } catch {
    showApp(false);
    if (authMode !== 'setup') {
      setAuthMode('login');
    }
    drawWheel();
  }
})();
