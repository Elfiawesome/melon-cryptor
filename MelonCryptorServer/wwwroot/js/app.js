document.addEventListener('DOMContentLoaded', () => {
	const statusDiv = document.getElementById('status');
	const vaultView = document.getElementById('vault-view');
	const openForm = document.getElementById('open-form');
	const errorDiv = document.getElementById('error-message');

	const vaultPathInput = document.getElementById('vaultPath');
	const passwordInput = document.getElementById('password');

	const openBtn = document.getElementById('openBtn');
	const newBtn = document.getElementById('newBtn');
	const uploadForm = document.getElementById('uploadForm');

	const fileTreeUl = document.getElementById('fileTree');

	// --- API Helper ---
	async function apiCall(endpoint, method = 'GET', body = null) {
		const options = {
			method,
			headers: {}
		};
		if (body) {
			if (body instanceof FormData) {
				// Let the browser set the Content-Type for FormData
			} else {
				options.headers['Content-Type'] = 'application/json';
				options.body = JSON.stringify(body);
			}
		}

		try {
			const response = await fetch(`/api${endpoint}`, options);
			if (!response.ok) {
				const errData = await response.json();
				throw new Error(errData.error || `HTTP error! status: ${response.status}`);
			}
			if (response.headers.get("content-type")?.includes("application/json")) {
				return await response.json();
			}
			return response; // For file downloads
		} catch (error) {
			console.error('API Call Error:', error);
			errorDiv.textContent = error.message;
			throw error;
		}
	}

	// --- UI Update Functions ---
	async function updateStatus() {
		errorDiv.textContent = '';
		try {
			const data = await apiCall('/vault/status');
			if (data.isOpen) {
				statusDiv.textContent = `✅ Vault Open: ${data.name}`;
				document.getElementById('vault-name').textContent = data.name;
				document.getElementById('vault-path').textContent = `Path: ${data.path}`;
				vaultView.classList.remove('hidden');
				openForm.classList.add('hidden');
				await refreshFileTree();
			} else {
				statusDiv.textContent = '❌ No vault is open.';
				vaultView.classList.add('hidden');
				openForm.classList.remove('hidden');
			}
		} catch (e) {
			statusDiv.textContent = 'Error getting status.';
		}
	}

	async function refreshFileTree() {
		const treeData = await apiCall('/vault/tree');
		fileTreeUl.innerHTML = ''; // Clear existing tree
		renderTree(treeData, fileTreeUl, []);
	}

	function renderTree(nodes, parentElement, currentPath) {
		nodes.forEach(node => {
			const li = document.createElement('li');
			li.textContent = node.Name;
			const nodePath = [...currentPath, node.Name];

			if (node.Type === 'file') {
				li.className = 'file';
				li.style.cursor = 'pointer';
				li.title = `Click to download. Path: ${JSON.stringify(nodePath)}`;
				li.onclick = () => downloadFile(nodePath);
			} else { // directory
				li.className = 'directory';
				if (node.Children && node.Children.length > 0) {
					const subUl = document.createElement('ul');
					renderTree(node.Children, subUl, nodePath);
					li.appendChild(subUl);
				}
			}
			parentElement.appendChild(li);
		});
	}

	// --- Event Handlers ---
	openBtn.addEventListener('click', async () => {
		const path = vaultPathInput.value;
		const password = passwordInput.value;
		if (!path || !password) {
			errorDiv.textContent = 'Path and password are required.';
			return;
		}
		await apiCall('/vault/open', 'POST', { Path: path, Password: password });
		updateStatus();
	});

	newBtn.addEventListener('click', async () => {
		const path = vaultPathInput.value;
		const password = passwordInput.value;
		if (!path || !password) {
			errorDiv.textContent = 'Path and password are required.';
			return;
		}
		await apiCall('/vault/new', 'POST', { Path: path, Password: password });
		updateStatus();
	});

	uploadForm.addEventListener('submit', async (e) => {
		e.preventDefault();
		const formData = new FormData(uploadForm);
		// We get the path as a string and need to validate it's a JSON array.
		// For simplicity here, we trust the input. In a real app, you'd validate.
		const pathString = formData.get('path');

		const dataToSend = new FormData();
		dataToSend.append('file', formData.get('file'));
		dataToSend.append('path', pathString);

		await apiCall('/files/stash', 'POST', dataToSend);
		uploadForm.reset();
		updateStatus();
	});

	async function downloadFile(path) {
		const response = await apiCall('/files/retrieve', 'POST', { Path: path });
		const blob = await response.blob();
		const url = window.URL.createObjectURL(blob);
		const a = document.createElement('a');
		a.style.display = 'none';
		a.href = url;
		a.download = path[path.length - 1];
		document.body.appendChild(a);
		a.click();
		window.URL.revokeObjectURL(url);
	}

	uploadForm.addEventListener('submit', async (e) => {
		e.preventDefault();
		const file = uploadForm.querySelector('input[type=file]').files[0];
		const pathString = uploadForm.querySelector('input[name=path]').value;

		if (!file) {
			errorDiv.textContent = 'Please select a file to upload.';
			return;
		}

		// This is the new part for the HttpListener backend
		try {
			// We construct the URL with query parameters for metadata
			const path = JSON.parse(pathString);
			const query = new URLSearchParams({
				path: JSON.stringify(path),
				filename: file.name
			});

			const endpoint = `/files/stash?${query.toString()}`;

			// We send the raw file data as the request body
			const response = await fetch(`/api${endpoint}`, {
				method: 'POST',
				headers: {
					'Content-Type': 'application/octet-stream'
				},
				body: file
			});

			if (!response.ok) {
				const errData = await response.json();
				throw new Error(errData.error || `HTTP error! status: ${response.status}`);
			}

			uploadForm.reset();
			await updateStatus(); // Refresh the view
		} catch (error) {
			console.error('Upload Error:', error);
			errorDiv.textContent = error.message;
		}
	});

	// In wwwroot/js/app.js

	// --- Initial Load ---
	updateStatus();
});