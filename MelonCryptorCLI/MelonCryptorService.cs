using System.Text;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;

namespace MelonCryptorCLI;

public class MelonCryptorService
{
	private const string ManifestFileName = "index.json.menc";

	private EncryptService? _encryptService;
	private VaultManifest? _manifest;

	public bool IsVaultOpen { get; private set; } = false;
	public string? VaultPath { get; private set; }
	public VaultManifest? Manifest => _manifest;

	public void OpenVault(string vaultPath, string password)
	{
		if (!Directory.Exists(vaultPath))
			throw new DirectoryNotFoundException($"Vault directory not found: {vaultPath}");

		var manifestPath = Path.Join(vaultPath, ManifestFileName);
		if (!File.Exists(manifestPath))
			throw new FileNotFoundException($"Vault manifest '{ManifestFileName}' not found. The directory may not be a valid vault.", manifestPath);

		try
		{
			VaultPath = vaultPath;
			_encryptService = new EncryptService(password);

			var encryptedManifestBytes = File.ReadAllBytes(manifestPath);
			var decryptedManifestJson = _encryptService.Decrypt(encryptedManifestBytes);

			_manifest = JsonSerializer.Deserialize<VaultManifest>(decryptedManifestJson);

			if (_manifest == null)
				throw new JsonException("Failed to deserialize the vault manifest.");

			IsVaultOpen = true;
			Console.WriteLine($"Vault opened successfully at: {vaultPath}");
		}
		catch (Exception ex) // Catches decryption errors (bad password) or JSON errors (corruption)
		{
			CloseAndReset();
			// Wrap the original exception for better debugging.
			throw new InvalidOperationException($"Failed to open vault at '{vaultPath}'. Ensure it is a valid vault and the password is correct.", ex);
		}
	}

	public void NewVault(string vaultPath, string password)
	{
		Directory.CreateDirectory(vaultPath);

		if (Directory.EnumerateFileSystemEntries(vaultPath).Any())
			throw new IOException($"Target directory is not empty: {vaultPath}");

		IsVaultOpen = true;
		VaultPath = vaultPath;
		_encryptService = new EncryptService(password);
		_manifest = new VaultManifest();

		SaveManifest();
		Console.WriteLine($"New vault created at: {vaultPath}");
	}

	public void StashFile(string sourceFilePath, List<string>? vaultDirectoryPath = null)
	{
		EnsureVaultIsOpen();

		if (!File.Exists(sourceFilePath))
			throw new FileNotFoundException($"Source file not found: {sourceFilePath}", nameof(sourceFilePath));

		var fileName = Path.GetFileName(sourceFilePath);
		var fileExt = Path.GetExtension(sourceFilePath);

		// 1. Find or create the target directory entry in the manifest.
		var targetDirectory = GetOrCreateDirectoryEntry(vaultDirectoryPath ?? []);

		if (targetDirectory.Files.ContainsKey(fileName))
			throw new InvalidOperationException($"A file named '{fileName}' already exists in the target vault directory.");

		// 2. Prepare the new file entry.
		var newFileId = _manifest.NextFileId++;
		var newFileEntry = new FileEntry(fileName, newFileId, targetDirectory.Id);
		var physicalFileName = $"{newFileId}{fileExt}.menc";

		// 3. Encrypt and store the physical file.
		var sourceData = File.ReadAllBytes(sourceFilePath);
		var encryptedData = _encryptService.Encrypt(sourceData);
		StoreFile(physicalFileName, encryptedData);

		// 4. Update the manifest and save it.
		targetDirectory.Files[fileName] = newFileEntry;
		SaveManifest();

		Console.WriteLine($"File '{fileName}' stashed to vault.");
	}

	public byte[] RetrieveFileBytes(List<string> vaultFilePath)
	{
		EnsureVaultIsOpen();

		var fileEntry = FindFileEntry(vaultFilePath)
			?? throw new FileNotFoundException($"The file '{string.Join('/', vaultFilePath)}' was not found in the vault.");

		var fileExt = Path.GetExtension(fileEntry.Name);
		var physicalFileName = $"{fileEntry.Id}{fileExt}.menc";

		var encryptedData = ReadVaultFileBytes(physicalFileName);
		var decryptedData = _encryptService.Decrypt(encryptedData);

		return decryptedData;
	}

	public void RetrieveFile(List<string> vaultFilePath, string destinationFilePath)
	{
		if (File.Exists(destinationFilePath))
			throw new IOException($"Destination file already exists: {destinationFilePath}");

		var decryptedData = RetrieveFileBytes(vaultFilePath);
		File.WriteAllBytes(destinationFilePath, decryptedData);
		Console.WriteLine($"File '{vaultFilePath.Last()}' retrieved to '{destinationFilePath}'.");
	}

	private void SaveManifest()
	{
		EnsureVaultIsOpen();

		var manifestJson = JsonSerializer.Serialize(_manifest, new JsonSerializerOptions { WriteIndented = true });
		var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
		var encryptedManifest = _encryptService.Encrypt(manifestBytes);

		StoreFile(ManifestFileName, encryptedManifest);

		// TODO : Remove in prod
		StoreFile("index.debug.json", manifestBytes);
	}

	public void MoveFile(List<string> sourceFilePath, List<string> destinationDirectoryPath)
	{
		EnsureVaultIsOpen();

		// 1. Find the source file and its original parent directory.
		var fileName = sourceFilePath.Last();
		var sourceDirectoryPath = sourceFilePath.Take(sourceFilePath.Count - 1).ToList();

		var sourceDirEntry = FindDirectoryEntry(sourceDirectoryPath)
			?? throw new DirectoryNotFoundException($"Source directory '{string.Join('/', sourceDirectoryPath)}' not found.");

		if (!sourceDirEntry.Files.TryGetValue(fileName, out var fileToMove))
			throw new FileNotFoundException($"Source file '{fileName}' not found in its directory.");

		// 2. Find the destination directory.
		var destinationDirEntry = FindDirectoryEntry(destinationDirectoryPath)
			?? throw new DirectoryNotFoundException($"Destination directory '{string.Join('/', destinationDirectoryPath)}' not found.");

		// 3. Check for naming conflicts.
		if (destinationDirEntry.Files.ContainsKey(fileName))
			throw new IOException($"A file named '{fileName}' already exists in the destination directory.");

		// 4. Perform the move in the manifest.
		sourceDirEntry.Files.Remove(fileName);
		fileToMove.ParentId = destinationDirEntry.Id;
		destinationDirEntry.Files.Add(fileName, fileToMove);

		// 5. Save the updated manifest.
		SaveManifest();
		Console.WriteLine($"Moved '{fileName}' to '{string.Join('/', destinationDirectoryPath)}'");
	}

	public void DeleteFile(List<string> filePath)
	{
		EnsureVaultIsOpen();

		var fileName = filePath.Last();
		var directoryPath = filePath.Take(filePath.Count - 1).ToList();

		var parentDir = FindDirectoryEntry(directoryPath)
			?? throw new DirectoryNotFoundException($"Directory for file '{string.Join('/', filePath)}' not found.");

		if (!parentDir.Files.TryGetValue(fileName, out var fileEntry))
			throw new FileNotFoundException($"File '{string.Join('/', filePath)}' not found in the vault.");

		// 1. Delete the physical file from disk.
		var fileExt = Path.GetExtension(fileEntry.Name);
		var physicalFileName = $"{fileEntry.Id}{fileExt}.menc";
		var physicalFilePath = Path.Join(VaultPath, physicalFileName);

		if (File.Exists(physicalFilePath))
		{
			File.Delete(physicalFilePath);
		}

		// 2. Remove the entry from the manifest.
		parentDir.Files.Remove(fileName);

		// 3. Save the changes.
		SaveManifest();
		Console.WriteLine($"Deleted file '{fileName}'.");
	}

	public void DeleteDirectory(List<string> dirPath)
	{
		EnsureVaultIsOpen();

		if (dirPath == null || dirPath.Count == 0)
			throw new InvalidOperationException("Cannot delete the root directory.");

		var dirName = dirPath.Last();
		var parentPath = dirPath.Take(dirPath.Count - 1).ToList();

		var parentDir = FindDirectoryEntry(parentPath)
			?? throw new DirectoryNotFoundException($"Parent directory not found for '{string.Join('/', dirPath)}'.");

		if (!parentDir.SubDirs.TryGetValue(dirName, out var dirToDelete))
			throw new DirectoryNotFoundException($"Directory '{string.Join('/', dirPath)}' not found.");

		if (dirToDelete.Files.Any() || dirToDelete.SubDirs.Any())
			throw new IOException("Directory is not empty. Cannot delete.");

		// Remove from manifest and save
		parentDir.SubDirs.Remove(dirName);
		SaveManifest();
		Console.WriteLine($"Deleted directory '{dirName}'.");
	}

	public void UpdateVaultMetadata(string? newName = null, string? newDescription = null)
	{
		EnsureVaultIsOpen();
		bool changed = false;
		if (newName != null)
		{
			_manifest.VaultName = newName;
			changed = true;
		}
		if (newDescription != null)
		{
			_manifest.Description = newDescription;
			changed = true;
		}

		if (changed)
		{
			SaveManifest();
			Console.WriteLine("Vault metadata updated.");
		}
	}

	public void UpdateFileDescription(List<string> filePath, string description)
	{
		EnsureVaultIsOpen();
		var fileEntry = FindFileEntry(filePath)
			?? throw new FileNotFoundException($"File '{string.Join('/', filePath)}' not found.");

		fileEntry.Description = description;
		SaveManifest();
		Console.WriteLine($"Description updated for file '{filePath.Last()}'.");
	}

	// --- Private Helper Methods ---

	private DirectoryEntry? FindDirectoryEntry(IEnumerable<string> path)
	{
		if (path == null) return null;

		var currentDir = _manifest?.Root;
		foreach (var dirName in path)
		{
			if (currentDir == null || !currentDir.SubDirs.TryGetValue(dirName, out currentDir))
			{
				return null; // Path does not exist
			}
		}
		return currentDir;
	}


	private DirectoryEntry GetOrCreateDirectoryEntry(IEnumerable<string> path)
	{
		EnsureVaultIsOpen();
		var currentDir = _manifest.Root;

		foreach (var dirName in path)
		{
			if (!currentDir.SubDirs.TryGetValue(dirName, out var nextDir))
			{
				// Directory does not exist, so create it.
				var newDirId = _manifest.NextDirId++;
				nextDir = new DirectoryEntry(dirName, newDirId, currentDir.Id);
				currentDir.SubDirs[dirName] = nextDir;
			}
			currentDir = nextDir;
		}
		return currentDir;
	}

	private FileEntry? FindFileEntry(List<string> path)
	{
		if (path == null || path.Count == 0) return null;

		var fileName = path.Last();
		var dirPath = path.Take(path.Count - 1);

		var currentDir = (DirectoryEntry?)_manifest!.Root;
		foreach (var dirName in dirPath)
		{
			if (currentDir == null || !currentDir.SubDirs.TryGetValue(dirName, out currentDir))
			{
				return null; // Path does not exist
			}
		}
		if (currentDir == null) { return null; }

		if (currentDir.Files.TryGetValue(fileName, out var fileEntry))
		{
			return fileEntry;
		}
		return null;
	}


	private byte[] ReadVaultFileBytes(string vaultRelativePath)
	{
		var targetPath = Path.Join(VaultPath, vaultRelativePath);
		if (!File.Exists(targetPath))
			throw new FileNotFoundException($"Physical file not found in vault: {targetPath}");
		return File.ReadAllBytes(targetPath);
	}

	private void StoreFile(string vaultRelativePath, byte[] data)
	{
		var targetPath = Path.Join(VaultPath, vaultRelativePath);
		File.WriteAllBytes(targetPath, data);
	}

	/// <summary>
	/// Ensures the vault is open, throwing an exception if it is not.
	/// Also uses attributes to help the C# compiler understand that key members are not null after this call.
	/// </summary>
	[MemberNotNull(nameof(VaultPath), nameof(_encryptService), nameof(_manifest))]
	private void EnsureVaultIsOpen()
	{
		if (!IsVaultOpen || VaultPath == null || _encryptService == null || _manifest == null)
			throw new InvalidOperationException("Vault is not open. Please open or create a vault first.");
	}

	private void CloseAndReset()
	{
		IsVaultOpen = false;
		VaultPath = null;
		_encryptService = null;
		_manifest = null;
	}
}

public class VaultManifest
{
	public string VaultName { get; set; } = "Untitled Vault";
	public string Description { get; set; } = "";
	public string Version { get; set; } = "1.0";
	public long NextFileId { get; set; } = 1;
	public long NextDirId { get; set; } = 1;

	public DirectoryEntry Root { get; set; } = new("root", 0, 0);
}

public class DirectoryEntry
{
	public DirectoryEntry(string name, long id, long parentId)
	{
		Name = name;
		Id = id;
		ParentId = parentId;
	}

	public string Name { get; set; }
	public long Id { get; set; }
	public long ParentId { get; set; }
	public string Description { get; set; } = "";

	// Maps logical file name (e.g., "photo.jpg") to its FileEntry
	public Dictionary<string, FileEntry> Files { get; set; } = [];

	// Maps logical directory name (e.g., "documents") to its DirectoryEntry
	public Dictionary<string, DirectoryEntry> SubDirs { get; set; } = [];
}

public class FileEntry
{
	public FileEntry(string name, long id, long parentId)
	{
		Name = name;
		Id = id;
		ParentId = parentId;
	}

	public string Name { get; set; }
	public long Id { get; set; }
	public long ParentId { get; set; }
	public string Description { get; set; } = "";
}