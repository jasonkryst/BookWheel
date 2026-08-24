(function () {
  const SUPPORTED_LOCALES = ['en', 'es', 'pl'];
  const DEFAULT_LOCALE = 'en';
  const LOCALE_STORAGE_KEY = 'bookwheel-locale';

  const TRANSLATIONS = {
    en: {
      common: {
        skipToContent: 'Skip to main content',
        cancel: 'Cancel',
        save: 'Save',
        saving: 'Saving...',
        close: 'Close',
        clear: 'Clear',
        remove: 'Remove',
        creating: 'Creating...',
        generating: 'Generating...',
        requestFailed: 'Request failed.',
        versionLabel: 'Version: {version}',
        unknownVersion: 'unknown',
        connectionError: 'Cannot connect to the server. Make sure the app is running, then try again.',
        offlineToast: 'You are offline. Some features will not work until you reconnect.',
        onlineToast: 'Back online.'
      },
      app: {
        title: 'Book Wheel'
      },
      auth: {
        loginTitle: 'Book Wheel Login',
        loginSubtitle: 'Log in with your existing account.',
        loginSubmit: 'Log in',
        usernameLabel: 'Username',
        passwordLabel: 'Password',
        logoutBtn: 'Log out',
        setupTitle: 'Create your Book Wheel account',
        setupSubtitle: 'No account exists yet. Create one to begin.',
        setupSubmit: 'Create account',
        setPasswordTitle: 'Set your Book Wheel password',
        setPasswordSubtitle: 'Use the secure reset link from your administrator.',
        newPasswordLabel: 'New password',
        confirmPasswordLabel: 'Confirm password',
        setPasswordSubmit: 'Set password',
        validatingLinkTitle: 'Validate password reset link',
        validatingLinkSubtitle: 'Checking your secure reset link...',
        setPasswordForUser: 'Set password for {username}',
        defaultAccountName: 'your account',
        linkExpiresAt: 'This link expires at {datetime}.',
        linkExpires24h: 'This link expires in 24 hours.',
        linkInvalidError: 'The password reset link is invalid or has expired.',
        linkValidatedToast: 'Reset link validated.',
        credentialsRequiredError: 'Username and password are required.',
        creatingAccountBusy: 'Creating account...',
        loggingInBusy: 'Logging in...',
        accountCreatedToast: 'Account created and signed in.',
        signedInToast: 'Signed in successfully.',
        signedOutToast: 'Signed out.',
        resetTokenMissingError: 'Reset token is missing.',
        bothPasswordsRequiredError: 'Both password fields are required.',
        passwordTooShortError: 'Password must be at least 8 characters.',
        passwordsMismatchError: 'Passwords do not match.',
        passwordUpdatedMessage: 'Password updated. You can now log in with your new password.',
        passwordUpdatedToast: 'Password updated successfully.'
      },
      theme: {
        switchThemeLabel: 'Switch theme',
        switchTo: 'Switch to {label}',
        dark: 'Dark mode',
        light: 'Light mode',
        highContrast: 'High contrast mode'
      },
      settings: {
        title: 'Settings',
        openLabel: 'Settings',
        themeLabel: 'Theme',
        languageLabel: 'Language',
        tabsLabel: 'Settings sections',
        manageUsersTab: 'Manage users',
        importExportTab: 'Import / Export',
        preferencesTab: 'Preferences'
      },
      wheel: {
        heading: 'Spin wheel',
        canvasAriaLabel: 'Book selection wheel. Press Enter or Space to spin.',
        spinBtn: 'Spin',
        spinningBusy: 'Spinning...',
        emptyStatus: 'Wheel is empty. Add books to spin.',
        summaryCount: 'Wheel has {count} books.',
        addBooksPrompt: 'Add books to spin',
        emptyPrompt: 'Add your first book to begin spinning.',
        addBooksError: 'Add books to spin.',
        selectionNotFoundError: 'Selected book was not found on the current wheel.',
        lastSelected: 'Last selected: {title}',
        selectedToast: 'Selected: {title}'
      },
      books: {
        heading: 'Active books',
        titleLabel: 'Book title',
        titlePlaceholder: 'Book title',
        isbnLabel: 'ISBN',
        isbnPlaceholder: 'ISBN (optional)',
        lookupBtn: 'Lookup',
        coverAlt: 'Cover of {title}',
        lookupNeedsInputError: 'Enter a title or ISBN to look up.',
        lookupSuccessMessage: 'Book details found. Review them before saving.',
        pickerTitle: 'Choose a match',
        pickerHint: 'Multiple books matched. Pick the right one to fill in its details.',
        addBtn: 'Add',
        prevPage: 'Previous',
        nextPage: 'Next',
        pageInfo: 'Page {current} of {total}',
        countOne: '1 book total',
        countOther: '{count} books total',
        editDialogTitle: 'Edit book title',
        deleteDialogTitle: 'Remove book',
        titleRequiredError: 'Book title is required.',
        titleEmptyError: 'Title cannot be empty.',
        updatedMessage: 'Book updated.',
        updatedToast: 'Book title updated.',
        removeConfirmMessage: 'Remove "{title}" from the active list?',
        removedMessage: 'Book removed from the active list.',
        removedToast: 'Book removed.',
        addedMessage: 'Book added.',
        addedToast: 'Book added to active list.',
        emptyHeading: 'No books yet',
        emptyHint1: 'Add your first title above to populate the wheel.',
        emptyHint2: 'Tip: after adding books, press the Spin button or hit Enter on the wheel.',
        editTitleHint: 'Edit this title',
        editAria: 'Edit book title: {title}',
        removeHint: 'Remove from active list',
        removeAria: 'Remove book: {title}',
        scannedBadgeTitle: 'Added via barcode scan',
        scannedBadgeLabel: 'Added via barcode scan',
        typeLabel: 'Book type',
        typePhysical: 'Physical',
        typeDigital: 'Digital',
        typeNookOnly: 'Nook Only'
      },
      users: {
        manageUsersBtn: 'Manage users',
        greeting: 'Hello, {username}',
        managementTabsLabel: 'User management options',
        createUserHeading: 'Create user',
        newAccountTag: 'New account',
        createUserDescription: 'Create the account, then share the generated setup link so the user can set their own password.',
        administratorLabel: 'Administrator',
        createUserBtn: 'Create user',
        existingUsersHeading: 'Existing users',
        directoryTag: 'Directory',
        updateDescription: 'Administrators can update other user accounts. Your own account is read-only here.',
        searchLabel: 'Search users',
        searchPlaceholder: 'Filter by username',
        accountStatusLabel: 'Account status',
        allAccounts: 'All accounts',
        needsAttention: 'Needs attention',
        zeroUsers: '0 users',
        totalUsers: '{count} users',
        filteredUsers: '{filtered} of {total} users',
        noMatchFilter: 'No users match this filter.',
        roleAdmin: 'Administrator',
        roleStandard: 'Standard user',
        createdOn: 'Created {date}',
        createdUnavailable: 'Created date unavailable',
        adminCheckbox: 'Admin',
        disabledCheckbox: 'Disabled',
        requireResetCheckbox: 'Require reset',
        lockedCheckbox: 'Locked',
        saveChangesAria: 'Save changes for {username}',
        removeUserAria: 'Remove user {username}',
        generateResetLinkBtn: 'Generate reset link',
        generateResetLinkAria: 'Generate password reset link for {username}',
        ownAccountHint: 'Use account settings for your own account updates.',
        cannotResetOwnHint: 'You cannot generate a reset link for the active account.',
        cannotRemoveOwnHint: 'You cannot remove the active account.',
        cannotRemoveFirstHint: 'The first account cannot be removed.',
        currentUserSuffix: '{username} (you)',
        usernameRequiredError: 'Username is required.',
        updatedUserMessage: 'Updated user {username}.',
        generatedResetLinkMessage: 'Generated a secure reset link for {username}.',
        generatedResetLinkToast: 'Reset link generated for {username}.',
        createdUserMessage: 'Created user {username}.',
        createdUserToast: 'Created user {username}.',
        deleteDialogTitle: 'Remove user account',
        deleteWarning: 'This action permanently removes the account and all books assigned to this user.',
        removeUserBtn: 'Remove user',
        deleteConfirmMessage: 'Remove user "{username}" and all of their books?',
        removedUserMessage: 'Removed user {username}. Deleted {count} books.',
        removedUserToast: 'Removed user {username}.',
        resetLinkDialogTitle: 'Password reset link',
        resetLinkShareLabel: 'Share this link with the user',
        copyLinkBtn: 'Copy link',
        resetLinkCreated: 'Reset link created for {username}. {expiryText}',
        linkExpiresAt: 'Link expires {datetime}.',
        linkExpires24h: 'Link expires in 24 hours.',
        noResetLinkError: 'No reset link is available.',
        linkCopiedMessage: 'Reset link copied to clipboard.',
        copyFailedMessage: 'Copy failed. Select and copy the link manually.'
      },
      transfer: {
        importExportLabel: 'Import or export books',
        dialogTitle: 'Import / Export books',
        importTab: 'Import',
        exportTab: 'Export',
        importDescription: 'Import JSON data and merge into your list. Existing title or ISBN matches, including previously deleted books, are skipped.',
        chooseFileLabel: 'Choose JSON file',
        importFileBtn: 'Import File',
        exportDescription: 'Download your current books, including ISBN/author/cover, deleted books, and spin history, as a JSON file.',
        downloadBtn: 'Download JSON',
        invalidJsonError: 'Invalid JSON format. Use an array or an object with a books array.',
        chooseFileError: 'Choose a JSON file to import.',
        emptyFileError: 'The selected file is empty.',
        noTitlesError: 'No valid titles found in JSON.',
        importCompleteMessage: 'Import complete. Added {added}, skipped {skipped} matches.',
        exportStartedMessage: 'Export file download started.',
        accountMismatchWarning: 'Note: this file was exported from a different account ({username}); it was imported into your current account.'
      },
      scanner: {
        heading: 'Scan barcode',
        hint: 'Point the camera at the book\'s barcode.',
        flipBtn: 'Flip camera',
        scanBtnTitle: 'Scan ISBN barcode',
        scanBtnLabel: 'Scan ISBN barcode',
        scanningStatus: 'Scanning…',
        detectedToast: 'Barcode detected.',
        notSupportedError: 'Barcode scanning is not supported on this device or browser.',
        cameraAccessError: 'Camera access was denied or is unavailable.'
      }
    },
    es: {
      common: {
        skipToContent: 'Saltar al contenido principal',
        cancel: 'Cancelar',
        save: 'Guardar',
        saving: 'Guardando...',
        close: 'Cerrar',
        clear: 'Borrar',
        remove: 'Eliminar',
        creating: 'Creando...',
        generating: 'Generando...',
        requestFailed: 'Error en la solicitud.',
        versionLabel: 'Versión: {version}',
        unknownVersion: 'desconocida',
        connectionError: 'No se puede conectar con el servidor. Asegúrate de que la aplicación esté en ejecución e inténtalo de nuevo.',
        offlineToast: 'Estás sin conexión. Algunas funciones no estarán disponibles hasta que te reconectes.',
        onlineToast: 'De nuevo en línea.'
      },
      app: {
        title: 'Book Wheel'
      },
      auth: {
        loginTitle: 'Inicio de sesión de Book Wheel',
        loginSubtitle: 'Inicia sesión con tu cuenta existente.',
        loginSubmit: 'Iniciar sesión',
        usernameLabel: 'Nombre de usuario',
        passwordLabel: 'Contraseña',
        logoutBtn: 'Cerrar sesión',
        setupTitle: 'Crea tu cuenta de Book Wheel',
        setupSubtitle: 'Todavía no existe ninguna cuenta. Crea una para empezar.',
        setupSubmit: 'Crear cuenta',
        setPasswordTitle: 'Establece tu contraseña de Book Wheel',
        setPasswordSubtitle: 'Usa el enlace seguro de restablecimiento proporcionado por tu administrador.',
        newPasswordLabel: 'Nueva contraseña',
        confirmPasswordLabel: 'Confirmar contraseña',
        setPasswordSubmit: 'Establecer contraseña',
        validatingLinkTitle: 'Validando enlace de restablecimiento de contraseña',
        validatingLinkSubtitle: 'Comprobando tu enlace seguro...',
        setPasswordForUser: 'Establecer contraseña para {username}',
        defaultAccountName: 'tu cuenta',
        linkExpiresAt: 'Este enlace caduca el {datetime}.',
        linkExpires24h: 'Este enlace caduca en 24 horas.',
        linkInvalidError: 'El enlace de restablecimiento de contraseña no es válido o ha caducado.',
        linkValidatedToast: 'Enlace de restablecimiento validado.',
        credentialsRequiredError: 'El nombre de usuario y la contraseña son obligatorios.',
        creatingAccountBusy: 'Creando cuenta...',
        loggingInBusy: 'Iniciando sesión...',
        accountCreatedToast: 'Cuenta creada e inicio de sesión realizado.',
        signedInToast: 'Sesión iniciada correctamente.',
        signedOutToast: 'Sesión cerrada.',
        resetTokenMissingError: 'Falta el token de restablecimiento.',
        bothPasswordsRequiredError: 'Ambos campos de contraseña son obligatorios.',
        passwordTooShortError: 'La contraseña debe tener al menos 8 caracteres.',
        passwordsMismatchError: 'Las contraseñas no coinciden.',
        passwordUpdatedMessage: 'Contraseña actualizada. Ya puedes iniciar sesión con tu nueva contraseña.',
        passwordUpdatedToast: 'Contraseña actualizada correctamente.'
      },
      theme: {
        switchThemeLabel: 'Cambiar tema',
        switchTo: 'Cambiar a {label}',
        dark: 'Modo oscuro',
        light: 'Modo claro',
        highContrast: 'Modo de alto contraste'
      },
      settings: {
        title: 'Configuración',
        openLabel: 'Configuración',
        themeLabel: 'Tema',
        languageLabel: 'Idioma',
        tabsLabel: 'Secciones de configuración',
        manageUsersTab: 'Gestionar usuarios',
        importExportTab: 'Importar / Exportar',
        preferencesTab: 'Preferencias'
      },
      wheel: {
        heading: 'Rueda giratoria',
        canvasAriaLabel: 'Rueda de selección de libros. Pulsa Intro o Espacio para girar.',
        spinBtn: 'Girar',
        spinningBusy: 'Girando...',
        emptyStatus: 'La rueda está vacía. Añade libros para girar.',
        summaryCount: 'La rueda tiene {count} libros.',
        addBooksPrompt: 'Añade libros para girar',
        emptyPrompt: 'Añade tu primer libro para empezar a girar.',
        addBooksError: 'Añade libros para girar.',
        selectionNotFoundError: 'El libro seleccionado no se encontró en la rueda actual.',
        lastSelected: 'Última selección: {title}',
        selectedToast: 'Seleccionado: {title}'
      },
      books: {
        heading: 'Libros activos',
        titleLabel: 'Título del libro',
        titlePlaceholder: 'Título del libro',
        isbnLabel: 'ISBN',
        isbnPlaceholder: 'ISBN (opcional)',
        lookupBtn: 'Buscar',
        coverAlt: 'Portada de {title}',
        lookupNeedsInputError: 'Introduce un título o un ISBN para buscar.',
        lookupSuccessMessage: 'Datos del libro encontrados. Revísalos antes de guardar.',
        pickerTitle: 'Elige una coincidencia',
        pickerHint: 'Se encontraron varios libros. Elige el correcto para completar sus datos.',
        addBtn: 'Añadir',
        prevPage: 'Anterior',
        nextPage: 'Siguiente',
        pageInfo: 'Página {current} de {total}',
        countOne: '1 libro en total',
        countOther: '{count} libros en total',
        editDialogTitle: 'Editar título del libro',
        deleteDialogTitle: 'Eliminar libro',
        titleRequiredError: 'El título del libro es obligatorio.',
        titleEmptyError: 'El título no puede estar vacío.',
        updatedMessage: 'Libro actualizado.',
        updatedToast: 'Título del libro actualizado.',
        removeConfirmMessage: '¿Eliminar "{title}" de la lista activa?',
        removedMessage: 'Libro eliminado de la lista activa.',
        removedToast: 'Libro eliminado.',
        addedMessage: 'Libro añadido.',
        addedToast: 'Libro añadido a la lista activa.',
        emptyHeading: 'Todavía no hay libros',
        emptyHint1: 'Añade tu primer título arriba para llenar la rueda.',
        emptyHint2: 'Consejo: después de añadir libros, pulsa el botón Girar o presiona Intro sobre la rueda.',
        editTitleHint: 'Editar este título',
        editAria: 'Editar título del libro: {title}',
        removeHint: 'Eliminar de la lista activa',
        removeAria: 'Eliminar libro: {title}',
        scannedBadgeTitle: 'Añadido mediante escaneo de código de barras',
        scannedBadgeLabel: 'Añadido mediante escaneo de código de barras',
        typeLabel: 'Tipo de libro',
        typePhysical: 'Físico',
        typeDigital: 'Digital',
        typeNookOnly: 'Solo Nook'
      },
      users: {
        manageUsersBtn: 'Gestionar usuarios',
        greeting: 'Hola, {username}',
        managementTabsLabel: 'Opciones de gestión de usuarios',
        createUserHeading: 'Crear usuario',
        newAccountTag: 'Cuenta nueva',
        createUserDescription: 'Crea la cuenta y comparte el enlace de configuración generado para que el usuario establezca su propia contraseña.',
        administratorLabel: 'Administrador',
        createUserBtn: 'Crear usuario',
        existingUsersHeading: 'Usuarios existentes',
        directoryTag: 'Directorio',
        updateDescription: 'Los administradores pueden actualizar otras cuentas de usuario. Tu propia cuenta es de solo lectura aquí.',
        searchLabel: 'Buscar usuarios',
        searchPlaceholder: 'Filtrar por nombre de usuario',
        accountStatusLabel: 'Estado de la cuenta',
        allAccounts: 'Todas las cuentas',
        needsAttention: 'Requiere atención',
        zeroUsers: '0 usuarios',
        totalUsers: '{count} usuarios',
        filteredUsers: '{filtered} de {total} usuarios',
        noMatchFilter: 'Ningún usuario coincide con este filtro.',
        roleAdmin: 'Administrador',
        roleStandard: 'Usuario estándar',
        createdOn: 'Creado el {date}',
        createdUnavailable: 'Fecha de creación no disponible',
        adminCheckbox: 'Admin',
        disabledCheckbox: 'Deshabilitado',
        requireResetCheckbox: 'Requerir restablecimiento',
        lockedCheckbox: 'Bloqueado',
        saveChangesAria: 'Guardar cambios de {username}',
        removeUserAria: 'Eliminar usuario {username}',
        generateResetLinkBtn: 'Generar enlace de restablecimiento',
        generateResetLinkAria: 'Generar enlace de restablecimiento de contraseña para {username}',
        ownAccountHint: 'Usa la configuración de la cuenta para actualizar tu propia cuenta.',
        cannotResetOwnHint: 'No puedes generar un enlace de restablecimiento para la cuenta activa.',
        cannotRemoveOwnHint: 'No puedes eliminar la cuenta activa.',
        cannotRemoveFirstHint: 'La primera cuenta no se puede eliminar.',
        currentUserSuffix: '{username} (tú)',
        usernameRequiredError: 'El nombre de usuario es obligatorio.',
        updatedUserMessage: 'Usuario {username} actualizado.',
        generatedResetLinkMessage: 'Se generó un enlace seguro de restablecimiento para {username}.',
        generatedResetLinkToast: 'Enlace de restablecimiento generado para {username}.',
        createdUserMessage: 'Usuario {username} creado.',
        createdUserToast: 'Usuario {username} creado.',
        deleteDialogTitle: 'Eliminar cuenta de usuario',
        deleteWarning: 'Esta acción elimina permanentemente la cuenta y todos los libros asignados a este usuario.',
        removeUserBtn: 'Eliminar usuario',
        deleteConfirmMessage: '¿Eliminar al usuario "{username}" y todos sus libros?',
        removedUserMessage: 'Usuario {username} eliminado. Se eliminaron {count} libros.',
        removedUserToast: 'Usuario {username} eliminado.',
        resetLinkDialogTitle: 'Enlace de restablecimiento de contraseña',
        resetLinkShareLabel: 'Comparte este enlace con el usuario',
        copyLinkBtn: 'Copiar enlace',
        resetLinkCreated: 'Enlace de restablecimiento creado para {username}. {expiryText}',
        linkExpiresAt: 'El enlace caduca el {datetime}.',
        linkExpires24h: 'El enlace caduca en 24 horas.',
        noResetLinkError: 'No hay ningún enlace de restablecimiento disponible.',
        linkCopiedMessage: 'Enlace de restablecimiento copiado al portapapeles.',
        copyFailedMessage: 'Error al copiar. Selecciona y copia el enlace manualmente.'
      },
      transfer: {
        importExportLabel: 'Importar o exportar libros',
        dialogTitle: 'Importar / Exportar libros',
        importTab: 'Importar',
        exportTab: 'Exportar',
        importDescription: 'Importa datos JSON y combínalos con tu lista. Se omiten las coincidencias de título o ISBN existentes, incluidos los libros eliminados anteriormente.',
        chooseFileLabel: 'Elegir archivo JSON',
        importFileBtn: 'Importar archivo',
        exportDescription: 'Descarga tus libros actuales, incluyendo ISBN/autor/portada, libros eliminados e historial de giros, como archivo JSON.',
        downloadBtn: 'Descargar JSON',
        invalidJsonError: 'Formato JSON no válido. Usa un array o un objeto con un array de libros.',
        chooseFileError: 'Elige un archivo JSON para importar.',
        emptyFileError: 'El archivo seleccionado está vacío.',
        noTitlesError: 'No se encontraron títulos válidos en el JSON.',
        importCompleteMessage: 'Importación completa. Añadidos {added}, omitidas {skipped} coincidencias.',
        exportStartedMessage: 'Se inició la descarga del archivo de exportación.',
        accountMismatchWarning: 'Nota: este archivo se exportó desde otra cuenta ({username}); se importó en tu cuenta actual.'
      },
      scanner: {
        heading: 'Escanear código de barras',
        hint: 'Apunte la cámara al código de barras del libro.',
        flipBtn: 'Cambiar cámara',
        scanBtnTitle: 'Escanear código ISBN',
        scanBtnLabel: 'Escanear código ISBN',
        scanningStatus: 'Escaneando…',
        detectedToast: 'Código de barras detectado.',
        notSupportedError: 'El escaneo de códigos de barras no está disponible en este dispositivo o navegador.',
        cameraAccessError: 'El acceso a la cámara fue denegado o no está disponible.'
      }
    },
    pl: {
      common: {
        skipToContent: 'Przejdź do głównej treści',
        cancel: 'Anuluj',
        save: 'Zapisz',
        saving: 'Zapisywanie...',
        close: 'Zamknij',
        clear: 'Wyczyść',
        remove: 'Usuń',
        creating: 'Tworzenie...',
        generating: 'Generowanie...',
        requestFailed: 'Żądanie nie powiodło się.',
        versionLabel: 'Wersja: {version}',
        unknownVersion: 'nieznana',
        connectionError: 'Nie można połączyć się z serwerem. Upewnij się, że aplikacja działa, a następnie spróbuj ponownie.',
        offlineToast: 'Jesteś offline. Niektóre funkcje będą niedostępne, dopóki nie połączysz się ponownie.',
        onlineToast: 'Znowu online.'
      },
      app: {
        title: 'Book Wheel'
      },
      auth: {
        loginTitle: 'Logowanie do Book Wheel',
        loginSubtitle: 'Zaloguj się na swoje istniejące konto.',
        loginSubmit: 'Zaloguj się',
        usernameLabel: 'Nazwa użytkownika',
        passwordLabel: 'Hasło',
        logoutBtn: 'Wyloguj się',
        setupTitle: 'Utwórz konto Book Wheel',
        setupSubtitle: 'Nie istnieje jeszcze żadne konto. Utwórz jedno, aby rozpocząć.',
        setupSubmit: 'Utwórz konto',
        setPasswordTitle: 'Ustaw hasło do Book Wheel',
        setPasswordSubtitle: 'Użyj bezpiecznego linku resetującego otrzymanego od administratora.',
        newPasswordLabel: 'Nowe hasło',
        confirmPasswordLabel: 'Potwierdź hasło',
        setPasswordSubmit: 'Ustaw hasło',
        validatingLinkTitle: 'Weryfikacja linku resetowania hasła',
        validatingLinkSubtitle: 'Sprawdzanie bezpiecznego linku...',
        setPasswordForUser: 'Ustaw hasło dla {username}',
        defaultAccountName: 'Twoje konto',
        linkExpiresAt: 'Ten link wygasa {datetime}.',
        linkExpires24h: 'Ten link wygasa za 24 godziny.',
        linkInvalidError: 'Link resetowania hasła jest nieprawidłowy lub wygasł.',
        linkValidatedToast: 'Link resetujący został zweryfikowany.',
        credentialsRequiredError: 'Nazwa użytkownika i hasło są wymagane.',
        creatingAccountBusy: 'Tworzenie konta...',
        loggingInBusy: 'Logowanie...',
        accountCreatedToast: 'Konto utworzone, zalogowano pomyślnie.',
        signedInToast: 'Zalogowano pomyślnie.',
        signedOutToast: 'Wylogowano.',
        resetTokenMissingError: 'Brak tokenu resetowania.',
        bothPasswordsRequiredError: 'Oba pola hasła są wymagane.',
        passwordTooShortError: 'Hasło musi mieć co najmniej 8 znaków.',
        passwordsMismatchError: 'Hasła nie są zgodne.',
        passwordUpdatedMessage: 'Hasło zaktualizowane. Możesz teraz zalogować się nowym hasłem.',
        passwordUpdatedToast: 'Hasło zostało pomyślnie zaktualizowane.'
      },
      theme: {
        switchThemeLabel: 'Zmień motyw',
        switchTo: 'Przełącz na {label}',
        dark: 'Tryb ciemny',
        light: 'Tryb jasny',
        highContrast: 'Tryb wysokiego kontrastu'
      },
      settings: {
        title: 'Ustawienia',
        openLabel: 'Ustawienia',
        themeLabel: 'Motyw',
        languageLabel: 'Język',
        tabsLabel: 'Sekcje ustawień',
        manageUsersTab: 'Zarządzaj użytkownikami',
        importExportTab: 'Importuj / Eksportuj',
        preferencesTab: 'Preferencje'
      },
      wheel: {
        heading: 'Koło losujące',
        canvasAriaLabel: 'Koło wyboru książki. Naciśnij Enter lub Spację, aby zakręcić.',
        spinBtn: 'Zakręć',
        spinningBusy: 'Kręcenie...',
        emptyStatus: 'Koło jest puste. Dodaj książki, aby zakręcić.',
        summaryCount: 'Koło zawiera {count} książek.',
        addBooksPrompt: 'Dodaj książki, aby zakręcić',
        emptyPrompt: 'Dodaj pierwszą książkę, aby zacząć losowanie.',
        addBooksError: 'Dodaj książki, aby zakręcić.',
        selectionNotFoundError: 'Wybrana książka nie została znaleziona na bieżącym kole.',
        lastSelected: 'Ostatnio wybrano: {title}',
        selectedToast: 'Wybrano: {title}'
      },
      books: {
        heading: 'Aktywne książki',
        titleLabel: 'Tytuł książki',
        titlePlaceholder: 'Tytuł książki',
        isbnLabel: 'ISBN',
        isbnPlaceholder: 'ISBN (opcjonalnie)',
        lookupBtn: 'Wyszukaj',
        coverAlt: 'Okładka: {title}',
        lookupNeedsInputError: 'Podaj tytuł lub numer ISBN, aby wyszukać.',
        lookupSuccessMessage: 'Znaleziono dane książki. Sprawdź je przed zapisaniem.',
        pickerTitle: 'Wybierz dopasowanie',
        pickerHint: 'Znaleziono wiele książek. Wybierz właściwą, aby uzupełnić jej dane.',
        addBtn: 'Dodaj',
        prevPage: 'Poprzednia',
        nextPage: 'Następna',
        pageInfo: 'Strona {current} z {total}',
        countOne: '1 książka łącznie',
        countOther: '{count} książek łącznie',
        editDialogTitle: 'Edytuj tytuł książki',
        deleteDialogTitle: 'Usuń książkę',
        titleRequiredError: 'Tytuł książki jest wymagany.',
        titleEmptyError: 'Tytuł nie może być pusty.',
        updatedMessage: 'Książka zaktualizowana.',
        updatedToast: 'Tytuł książki zaktualizowany.',
        removeConfirmMessage: 'Usunąć „{title}" z listy aktywnych?',
        removedMessage: 'Książka usunięta z listy aktywnych.',
        removedToast: 'Książka usunięta.',
        addedMessage: 'Książka dodana.',
        addedToast: 'Książka dodana do listy aktywnych.',
        emptyHeading: 'Brak książek',
        emptyHint1: 'Dodaj pierwszy tytuł powyżej, aby zapełnić koło.',
        emptyHint2: 'Wskazówka: po dodaniu książek naciśnij przycisk Zakręć lub Enter na kole.',
        editTitleHint: 'Edytuj ten tytuł',
        editAria: 'Edytuj tytuł książki: {title}',
        removeHint: 'Usuń z listy aktywnych',
        removeAria: 'Usuń książkę: {title}',
        scannedBadgeTitle: 'Dodano przez skanowanie kodu kreskowego',
        scannedBadgeLabel: 'Dodano przez skanowanie kodu kreskowego',
        typeLabel: 'Typ książki',
        typePhysical: 'Fizyczna',
        typeDigital: 'Cyfrowa',
        typeNookOnly: 'Tylko Nook'
      },
      users: {
        manageUsersBtn: 'Zarządzaj użytkownikami',
        greeting: 'Witaj, {username}',
        managementTabsLabel: 'Opcje zarządzania użytkownikami',
        createUserHeading: 'Utwórz użytkownika',
        newAccountTag: 'Nowe konto',
        createUserDescription: 'Utwórz konto, a następnie udostępnij wygenerowany link konfiguracyjny, aby użytkownik mógł ustawić własne hasło.',
        administratorLabel: 'Administrator',
        createUserBtn: 'Utwórz użytkownika',
        existingUsersHeading: 'Istniejący użytkownicy',
        directoryTag: 'Katalog',
        updateDescription: 'Administratorzy mogą aktualizować inne konta użytkowników. Twoje własne konto jest tutaj tylko do odczytu.',
        searchLabel: 'Szukaj użytkowników',
        searchPlaceholder: 'Filtruj według nazwy użytkownika',
        accountStatusLabel: 'Stan konta',
        allAccounts: 'Wszystkie konta',
        needsAttention: 'Wymaga uwagi',
        zeroUsers: '0 użytkowników',
        totalUsers: '{count} użytkowników',
        filteredUsers: '{filtered} z {total} użytkowników',
        noMatchFilter: 'Żaden użytkownik nie pasuje do tego filtra.',
        roleAdmin: 'Administrator',
        roleStandard: 'Użytkownik standardowy',
        createdOn: 'Utworzono {date}',
        createdUnavailable: 'Data utworzenia niedostępna',
        adminCheckbox: 'Admin',
        disabledCheckbox: 'Wyłączony',
        requireResetCheckbox: 'Wymagaj resetu',
        lockedCheckbox: 'Zablokowany',
        saveChangesAria: 'Zapisz zmiany dla {username}',
        removeUserAria: 'Usuń użytkownika {username}',
        generateResetLinkBtn: 'Wygeneruj link resetujący',
        generateResetLinkAria: 'Wygeneruj link resetowania hasła dla {username}',
        ownAccountHint: 'Użyj ustawień konta, aby zaktualizować własne konto.',
        cannotResetOwnHint: 'Nie możesz wygenerować linku resetującego dla aktywnego konta.',
        cannotRemoveOwnHint: 'Nie możesz usunąć aktywnego konta.',
        cannotRemoveFirstHint: 'Pierwszego konta nie można usunąć.',
        currentUserSuffix: '{username} (Ty)',
        usernameRequiredError: 'Nazwa użytkownika jest wymagana.',
        updatedUserMessage: 'Zaktualizowano użytkownika {username}.',
        generatedResetLinkMessage: 'Wygenerowano bezpieczny link resetujący dla {username}.',
        generatedResetLinkToast: 'Wygenerowano link resetujący dla {username}.',
        createdUserMessage: 'Utworzono użytkownika {username}.',
        createdUserToast: 'Utworzono użytkownika {username}.',
        deleteDialogTitle: 'Usuń konto użytkownika',
        deleteWarning: 'Ta czynność trwale usuwa konto i wszystkie książki przypisane do tego użytkownika.',
        removeUserBtn: 'Usuń użytkownika',
        deleteConfirmMessage: 'Usunąć użytkownika „{username}" i wszystkie jego książki?',
        removedUserMessage: 'Usunięto użytkownika {username}. Usunięto {count} książek.',
        removedUserToast: 'Usunięto użytkownika {username}.',
        resetLinkDialogTitle: 'Link resetowania hasła',
        resetLinkShareLabel: 'Udostępnij ten link użytkownikowi',
        copyLinkBtn: 'Skopiuj link',
        resetLinkCreated: 'Utworzono link resetujący dla {username}. {expiryText}',
        linkExpiresAt: 'Link wygasa {datetime}.',
        linkExpires24h: 'Link wygasa za 24 godziny.',
        noResetLinkError: 'Brak dostępnego linku resetującego.',
        linkCopiedMessage: 'Link resetujący skopiowany do schowka.',
        copyFailedMessage: 'Kopiowanie nie powiodło się. Zaznacz i skopiuj link ręcznie.'
      },
      transfer: {
        importExportLabel: 'Importuj lub eksportuj książki',
        dialogTitle: 'Importuj / eksportuj książki',
        importTab: 'Import',
        exportTab: 'Eksport',
        importDescription: 'Zaimportuj dane JSON i połącz je z listą. Pasujące tytuły lub numery ISBN, w tym wcześniej usunięte książki, są pomijane.',
        chooseFileLabel: 'Wybierz plik JSON',
        importFileBtn: 'Importuj plik',
        exportDescription: 'Pobierz swoje bieżące książki, w tym ISBN/autora/okładkę, usunięte książki i historię losowań, jako plik JSON.',
        downloadBtn: 'Pobierz JSON',
        invalidJsonError: 'Nieprawidłowy format JSON. Użyj tablicy lub obiektu z tablicą książek.',
        chooseFileError: 'Wybierz plik JSON do zaimportowania.',
        emptyFileError: 'Wybrany plik jest pusty.',
        noTitlesError: 'Nie znaleziono prawidłowych tytułów w pliku JSON.',
        importCompleteMessage: 'Import zakończony. Dodano {added}, pominięto {skipped} dopasowań.',
        exportStartedMessage: 'Rozpoczęto pobieranie pliku eksportu.',
        accountMismatchWarning: 'Uwaga: ten plik wyeksportowano z innego konta ({username}); zaimportowano go na Twoje bieżące konto.'
      },
      scanner: {
        heading: 'Skanuj kod kreskowy',
        hint: 'Skieruj kamerę na kod kreskowy książki.',
        flipBtn: 'Przełącz kamerę',
        scanBtnTitle: 'Skanuj kod ISBN',
        scanBtnLabel: 'Skanuj kod ISBN',
        scanningStatus: 'Skanowanie…',
        detectedToast: 'Wykryto kod kreskowy.',
        notSupportedError: 'Skanowanie kodów kreskowych nie jest obsługiwane na tym urządzeniu ani w tej przeglądarce.',
        cameraAccessError: 'Dostęp do kamery został zablokowany lub jest niedostępny.'
      }
    }
  };

  function detectInitialLocale() {
    const stored = localStorage.getItem(LOCALE_STORAGE_KEY);
    if (SUPPORTED_LOCALES.includes(stored)) {
      return stored;
    }

    const browserLocales = navigator.languages && navigator.languages.length
      ? navigator.languages
      : [navigator.language];

    for (const browserLocale of browserLocales) {
      const shortCode = (browserLocale || '').slice(0, 2).toLowerCase();
      if (SUPPORTED_LOCALES.includes(shortCode)) {
        return shortCode;
      }
    }

    return DEFAULT_LOCALE;
  }

  let currentLocale = detectInitialLocale();

  function resolveCatalogValue(locale, key) {
    const segments = key.split('.');
    let node = TRANSLATIONS[locale];
    for (const segment of segments) {
      if (!node || typeof node !== 'object') {
        return undefined;
      }
      node = node[segment];
    }
    return typeof node === 'string' ? node : undefined;
  }

  function interpolate(template, params) {
    if (!params) {
      return template;
    }
    return template.replace(/\{(\w+)\}/g, (match, token) =>
      Object.prototype.hasOwnProperty.call(params, token) ? String(params[token]) : match
    );
  }

  function t(key, params) {
    const value = resolveCatalogValue(currentLocale, key)
      ?? resolveCatalogValue(DEFAULT_LOCALE, key)
      ?? key;
    return interpolate(value, params);
  }

  function applyStaticTranslations(root) {
    const scope = root || document;

    scope.querySelectorAll('[data-i18n]').forEach(element => {
      element.textContent = t(element.getAttribute('data-i18n'));
    });
    scope.querySelectorAll('[data-i18n-placeholder]').forEach(element => {
      element.setAttribute('placeholder', t(element.getAttribute('data-i18n-placeholder')));
    });
    scope.querySelectorAll('[data-i18n-aria-label]').forEach(element => {
      element.setAttribute('aria-label', t(element.getAttribute('data-i18n-aria-label')));
    });
    scope.querySelectorAll('[data-i18n-title]').forEach(element => {
      element.setAttribute('title', t(element.getAttribute('data-i18n-title')));
    });
  }

  function getCurrentLocale() {
    return currentLocale;
  }

  function setLocale(locale) {
    if (!SUPPORTED_LOCALES.includes(locale)) {
      return;
    }

    currentLocale = locale;
    localStorage.setItem(LOCALE_STORAGE_KEY, locale);
    document.documentElement.setAttribute('lang', locale);
    applyStaticTranslations(document);
    window.dispatchEvent(new CustomEvent('bookwheel:locale-changed', { detail: { locale } }));
  }

  document.addEventListener('DOMContentLoaded', () => {
    document.documentElement.setAttribute('lang', currentLocale);
    applyStaticTranslations(document);
  });

  window.BookWheelI18n = {
    t,
    setLocale,
    getCurrentLocale,
    applyStaticTranslations,
    SUPPORTED_LOCALES
  };
})();
