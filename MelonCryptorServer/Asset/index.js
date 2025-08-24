document.addEventListener('DOMContentLoaded', function () {
	const accessFormElement = document.querySelector('.access-form');
	const accessFormOpenVaultButtonElement = document.getElementById('open-vault-submit');
	const accessFormcreateVaultButtonElement = document.getElementById('create-vault-submit');

	const vaultPathInputElement = document.querySelector('#vault-path-input');
	const passwordInputElement = document.querySelector('#password-input');
	const viewContainerElement = document.querySelector('.main-container.bottom');

	var vaultPath;
	var password;
	var currentDirectoryData;

	function openVault(_vaultPath, _password) {
		// Update global value
		vaultPath = _vaultPath
		password = _password

		// Make table
		fetch('vault-window.html').then(data => {
			return data.text()
		}).then(data => {
			// Update with default template
			viewContainerElement.innerHTML = ''
			viewContainerElement.innerHTML = data;
			var tableContentContainer = viewContainerElement.querySelector('.table-content-container');
			updateVaultTable(tableContentContainer)

			const addFilesButtonElement = viewContainerElement.querySelector('.toolbar-button.add-files-button');
			addFilesButtonElement.addEventListener('click', function () {
				window.showOpenFilePicker({ multiple: true }).then(
					(fileHandles) => {
						fileHandles.forEach(item => {
							item.getFile().then(file => {
								uploadFile(file)
							}).then(response => {
								if (response.ok) { updateVaultTable(tableContentContainer); }
							})
						})
					}
				);
			});

			const addVaultButtonElement = viewContainerElement.querySelector('.toolbar-button.add-vault-button');
			addVaultButtonElement.addEventListener('click', function () {
				const formData = new FormData();
				formData.append('vault', vaultPath);
				formData.append('password', password);
				formData.append('name', 'New Vault');

				const queryString = new URLSearchParams(formData).toString();
				const url = `/api/add-vault?${queryString}`;

				return fetch(url, { method: 'POST' });
			});

		});
	}

	function createVault(_vaultPath, _password) {
		const formData = new FormData();
		formData.append('vault', _vaultPath);
		formData.append('password', _password);

		const queryString = new URLSearchParams(formData).toString();
		const url = `/api/create-vault?${queryString}`;

		fetch(url, { method: 'POST' }).then(response => {
			if (response.ok) { openVault(_vaultPath, _password); }
		});
	}

	function updateVaultTable(tableContainerElement) {
		tableContainerElement.innerHTML = ''

		const formData = new FormData();
		formData.append('vault', vaultPath);
		formData.append('password', password);

		const queryString = new URLSearchParams(formData).toString();
		const url = `/api/get-vault?${queryString}`;

		fetch(url).then(response => {
			if (response.ok) { return response.json(); }
			else { return null; }
		}).then(data => {
			if (data == null) { return; }
			addBackItemVaultTable(tableContainerElement);
			data.forEach(item => {
				addDirItemToVaultTable(tableContainerElement, item);
			});
		})
	}

	function addDirItemToVaultTable(tableElement, directoryData) {
		const fileName = directoryData['name'];
		const encryptedFileName = directoryData['encrypted_name']
		const fileExt = fileName.split('.').pop();
		const dirType = directoryData['type'];

		var iconName = ''
		if (dirType == 1) { iconName = fileExtToMaterialIcon(fileExt) } else { iconName = 'folder' }

		const rowElement = buildVaultTable(
			iconName, fileName, '', ''
		);
		tableElement.appendChild(rowElement);

		if (dirType == 1) {
			// File
			rowElement.addEventListener('click', function () {
				getFileData(encryptedFileName).then(data => {
					const link = document.createElement('a');
					link.href = URL.createObjectURL(data);
					link.download = fileName;
					link.click();
				})
			});
		} else {
			// Folders
			rowElement.addEventListener('click', function () {
				openVault(vaultPath + '/' + encryptedFileName, password);
			});
		}
	}

	function addBackItemVaultTable(tableElement) {
		const rowElement = buildVaultTable(
			'folder_open', '...', '', ''
		);
		tableElement.appendChild(rowElement);

		rowElement.addEventListener('click', function () {
			openVault(vaultPath.split('/').slice(0, -1).join('/'), password);
		})
	}

	function buildVaultTable(materialIconName, fileName, fileDate, fileSize) {
		const row = document.createElement('div');
		row.className = 'table-row';
		row.innerHTML = `
		<div class="table-cell-name">
			<i class="material-icons">${materialIconName}</i>
			<span>${fileName}</span>
		</div>
		<div class="table-cell-date">${fileDate}</div>
		<div class="table-cell-size">${fileSize}</div>`;
		return row;
	}

	function uploadFile(file) {
		const formData = new FormData();
		formData.append('vault', vaultPath);
		formData.append('password', password);

		const queryString = new URLSearchParams(formData).toString();
		const url = `/api/upload-file?${queryString}`;

		return fetch(url, {
			method: 'POST',
			body: file,
			headers: {
				"filename": file.name ?? "untitled"
			},
		});
	}

	function getFileData(encryptedFileName) {
		const formData = new FormData();
		formData.append('vault', vaultPath);
		formData.append('password', password);
		formData.append('encrypted-filename', encryptedFileName);

		const queryString = new URLSearchParams(formData).toString();
		const url = `/api/get-vault-file?${queryString}`;


		return fetch(url).then(response => {
			if (response.ok) {
				return response.blob();
			} else {
				return null;
			}
		});
	}

	function fileExtToMaterialIcon(ext) {
		switch (ext.toLowerCase()) {
			case 'jpg': case 'jpeg': case 'png': case 'gif': case 'bmp': case 'webp': case 'svg':
				return 'image';
			case 'mp4': case 'mov': case 'avi': case 'mkv': case 'webm':
				return 'movie';
			case 'mp3': case 'wav': case 'ogg': case 'flac':
				return 'audio_file';
			case 'pdf':
				return 'picture_as_pdf';
			case 'doc': case 'docx': case 'rtf':
				return 'description';
			case 'xls': case 'xlsx': case 'csv':
				return 'table_chart';
			case 'ppt': case 'pptx':
				return 'slideshow';
			case 'txt': case 'log':
				return 'text_snippet';
			case 'zip': case 'rar': case '7z': case 'tar': case 'gz':
				return 'folder_zip';
			case 'json': case 'xml':
				return 'data_object'; case 'js': case 'html': case 'css': case 'py': case 'java': case 'cpp': case 'c': case 'php': case 'ts': case 'jsx':
			case 'tsx': return 'code';
			default:
				return 'insert_drive_file'; // Generic file icon
		}
	}


	accessFormOpenVaultButtonElement.addEventListener('click', function () {
		openVault(vaultPathInputElement.value, passwordInputElement.value);
	});

	accessFormcreateVaultButtonElement.addEventListener('click', function () {
		createVault(vaultPathInputElement.value, passwordInputElement.value);
	});

	accessFormElement.addEventListener('submit', function (event) {
		// Prevents the form from submitting and reloading the page
		event.preventDefault();
		return;
	});
});