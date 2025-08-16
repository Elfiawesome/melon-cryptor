document.addEventListener('DOMContentLoaded', () => {
	const passwordInput = document.getElementById('passwordInput');
	const openVaultBtn = document.getElementById('openVaultBtn');
	const statusDiv = document.getElementById('status');
	const fileListContainer = document.querySelector('.file-list-container');
	const fileListBody = document.querySelector('#fileList tbody');

	let vaultDirectoryHandle = null;
	let vaultKey = null;

	// --- Core Cryptography Functions ---

	/**
	 * Derives a 32-byte (256-bit) AES key from a password string using SHA-256.
	 * This matches the C# implementation.
	 * @param {string} password The user's password.
	 * @returns {Promise<CryptoKey>} The derived key for use with Web Crypto API.
	 */
	async function deriveKey(password) {
		const encoder = new TextEncoder();
		const passwordBuffer = encoder.encode(password);
		const hashBuffer = await crypto.subtle.digest('SHA-256', passwordBuffer);
		// We import the key for use in AES-CBC decryption.
		return crypto.subtle.importKey('raw', hashBuffer, 'AES-CBC', false, ['decrypt']);
	}

	/**
	 * Decrypts data using AES-CBC. It assumes the first 16 bytes of the
	 * encryptedData are the IV, matching the C# implementation.
	 * @param {CryptoKey} key The key derived from the password.
	 * @param {ArrayBuffer} encryptedData The raw encrypted data with prepended IV.
	 * @returns {Promise<ArrayBuffer>} The decrypted data.
	 */
	async function decryptData(key, encryptedData) {
		const iv = encryptedData.slice(0, 16); // AES block size is 128 bits = 16 bytes
		const ciphertext = encryptedData.slice(16);

		return crypto.subtle.decrypt(
			{ name: 'AES-CBC', iv: iv },
			key,
			ciphertext
		);
	}

	// --- File System and UI Functions ---

	/**
	 * Reads a file from the selected vault directory.
	 * @param {FileSystemDirectoryHandle} dirHandle The handle to the vault directory.
	 * @param {string} filePath The relative path to the file (e.g., "index.menc.json" or "default/1.menc.txt").
	 * @returns {Promise<ArrayBuffer>} The file content as an ArrayBuffer.
	 */
	async function readFileFromVault(dirHandle, filePath) {
		try {
			// Handle nested paths like "default/1.menc.txt"
			const pathParts = filePath.split(/[/\\]/);
			let currentHandle = dirHandle;
			for (let i = 0; i < pathParts.length - 1; i++) {
				currentHandle = await currentHandle.getDirectoryHandle(pathParts[i]);
			}
			const fileHandle = await currentHandle.getFileHandle(pathParts[pathParts.length - 1]);
			const file = await fileHandle.getFile();
			return await file.arrayBuffer();
		} catch (error) {
			console.error(`Error reading file "${filePath}":`, error);
			throw new Error(`Could not find or read the file: ${filePath}`);
		}
	}

	/**
	 * Displays a message to the user.
	 * @param {string} message The message to show.
	 * @param {'error' | 'success' | 'info'} type The type of message.
	 */
	function showStatus(message, type = 'info') {
		statusDiv.textContent = message;
		statusDiv.className = `status ${type}`;
	}

	/**
	 * Renders the list of files in the UI table.
	 * @param {object} indexData The parsed JSON from the index file.
	 */
	function renderFileList(indexData) {
		fileListBody.innerHTML = ''; // Clear previous entries
		if (!indexData || !indexData.Files || Object.keys(indexData.Files).length === 0) {
			const row = fileListBody.insertRow();
			const cell = row.insertCell();
			cell.colSpan = 3;
			cell.textContent = 'Vault is empty.';
			return;
		}

		for (const [vaultPath, fileModel] of Object.entries(indexData.Files)) {
			const row = fileListBody.insertRow();
			row.insertCell().textContent = fileModel.FileName;
			row.insertCell().textContent = fileModel.VaultPath;

			const actionCell = row.insertCell();
			const downloadBtn = document.createElement('button');
			downloadBtn.textContent = 'Download';
			downloadBtn.dataset.vaultPath = vaultPath;
			downloadBtn.dataset.fileName = fileModel.FileName;
			actionCell.appendChild(downloadBtn);
		}
		fileListContainer.style.display = 'block';
	}

	/**
	 * Triggers a browser download for the given data.
	 * @param {string} filename The desired name of the downloaded file.
	 * @param {ArrayBuffer} data The raw data to be downloaded.
	 */
	function downloadFile(filename, data) {
		const blob = new Blob([data]);
		const url = URL.createObjectURL(blob);
		const a = document.createElement('a');
		a.href = url;
		a.download = filename;
		document.body.appendChild(a);
		a.click();
		document.body.removeChild(a);
		URL.revokeObjectURL(url); // Clean up
	}


	// --- Event Handlers ---

	openVaultBtn.addEventListener('click', async () => {
		const password = passwordInput.value;
		if (!password) {
			showStatus('Please enter a password.', 'error');
			return;
		}

		if (!window.showDirectoryPicker) {
			showStatus('Your browser does not support the File System Access API. Please use a modern browser like Chrome or Edge.', 'error');
			return;
		}

		try {
			showStatus('Processing...', 'info');
			vaultDirectoryHandle = await window.showDirectoryPicker();
			vaultKey = await deriveKey(password);

			const encryptedIndexData = await readFileFromVault(vaultDirectoryHandle, 'index.menc.json');
			const decryptedIndexData = await decryptData(vaultKey, encryptedIndexData);

			const decoder = new TextDecoder();
			const indexJson = decoder.decode(decryptedIndexData);
			const indexData = JSON.parse(indexJson);

			if (!indexData.Success) {
				throw new Error("Decryption failed. Invalid password or corrupted index file.");
			}

			renderFileList(indexData);
			showStatus('Vault opened successfully!', 'success');

		} catch (error) {
			console.error(error);
			showStatus(`Error: ${error.message}`, 'error');
			fileListContainer.style.display = 'none';
		}
	});

	fileListBody.addEventListener('click', async (event) => {
		if (event.target.tagName !== 'BUTTON') return;

		const button = event.target;
		const vaultPath = button.dataset.vaultPath;
		const fileName = button.dataset.fileName;

		if (!vaultPath || !vaultKey || !vaultDirectoryHandle) {
			showStatus('Vault is not properly loaded. Please try opening it again.', 'error');
			return;
		}

		try {
			showStatus(`Decrypting ${fileName}...`, 'info');
			button.disabled = true;
			button.textContent = 'Decrypting...';

			const encryptedFileData = await readFileFromVault(vaultDirectoryHandle, vaultPath);
			const decryptedFileData = await decryptData(vaultKey, encryptedFileData);

			downloadFile(fileName, decryptedFileData);

			showStatus(`${fileName} decrypted and download started.`, 'success');

		} catch (error) {
			console.error(error);
			showStatus(`Failed to retrieve file: ${error.message}`, 'error');
		} finally {
			button.disabled = false;
			button.textContent = 'Download';
		}
	});
});