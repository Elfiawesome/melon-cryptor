using System.Text.Json;
using MelonCryptor.Encryption;
using MelonCryptor.Model;

namespace MelonCryptor;

public class Vault
{
	public bool UseEncryption { get; private set; } = true; // To do set optional encryption for debugging later on
	public int MaxFileSizeBytes { get; set; } = (1024 * 1024) * 99;
	private readonly string _path;
	private readonly IEncryptionService _encryptionService;
	private readonly string _password;
	private IndexModel _index = new();
	private const string IndexFileName = "index.vault";
	private const string FileExtension = ".encs";

	private Vault(string path, string password, IEncryptionService encryptionService)
	{
		_path = path;
		_password = password;
		_encryptionService = encryptionService;
	}

	// Constructor Methods
	public static Vault Create(string path, string password, IEncryptionService encryptionService)
	{
		if (Directory.Exists(path) && File.Exists(Path.Combine(path, IndexFileName)))
		{
			throw new InvalidOperationException("A vault already exists at this path.");
		}

		Directory.CreateDirectory(path);
		var vault = new Vault(path, password, encryptionService);
		vault.SaveChanges(); // Create the empty index
		return vault;
	}

	public static Vault Open(string path, string password, IEncryptionService encryptionService)
	{
		if (!Directory.Exists(path) || !File.Exists(Path.Combine(path, IndexFileName)))
		{
			throw new InvalidOperationException("No vault found at this path.");
		}

		var vault = new Vault(path, password, encryptionService);
		vault.LoadIndex();
		return vault;
	}

	public static Vault OpenCreate(string path, string password, IEncryptionService encryptionService)
	{
		if (!Directory.Exists(path))
		{
			throw new InvalidOperationException("Invalid Directory");
		}
		if (File.Exists(Path.Combine(path, IndexFileName)))
		{
			return Open(path, password, encryptionService);
		}
		else
		{
			return Create(path, password, encryptionService);
		}
	}


	public void EditFileMeta(string encryptedPath, string? newName = null, string? newDescription = null)
	{
		if (!_index.Files.ContainsKey(encryptedPath)) { return; }
		if (newName != null) { _index.Vaults[encryptedPath].Name = newName; }
		if (newDescription != null) { _index.Vaults[encryptedPath].Description = newDescription; }
		SaveChanges();
	}

	public void EditVaultMeta(string encryptedPath, string? newName = null, string? newDescription = null)
	{
		if (!_index.Vaults.ContainsKey(encryptedPath)) { return; }
		if (newName != null) { _index.Vaults[encryptedPath].Name = newName; }
		if (newDescription != null) { _index.Vaults[encryptedPath].Description = newDescription; }
		SaveChanges();
	}

	// Loads disk index to memory
	private void LoadIndex()
	{
		var indexPath = Path.Combine(_path, IndexFileName);
		var encryptedIndex = File.ReadAllBytes(indexPath);
		try
		{
			var decryptedIndexJson = _encryptionService.Decrypt(encryptedIndex, _password);
			_index = JsonSerializer.Deserialize<IndexModel>(decryptedIndexJson) ?? throw new IOException("Could not deserialize vault index");
		}
		catch
		{
			throw new IOException("Could not decrypt/deserialize vault index. The vault index may be corrupted");
		}
	}

	// Updates loaded index to disk
	private void SaveChanges()
	{
		var indexPath = Path.Combine(_path, IndexFileName);
		var indexJson = JsonSerializer.Serialize(_index) ?? throw new IOException("Could not serialize vault index");
		var encryptedIndex = _encryptionService.Encrypt(indexJson, _password);
		File.WriteAllBytes(indexPath, encryptedIndex);
		// DEBUG TODO: to remove!
		// File.WriteAllText(Path.Combine(_path, "index-debug.json"), indexJson);
	}

	private string GenerateId()
	{
		var newId = _index.IdCounter++;
		SaveChanges();
		return newId.ToString();
	}

	// Add a file
	public void AddFile(string sourceFilePath)
	{
		var fileName = Path.GetFileName(sourceFilePath);
		var fileContent = File.ReadAllBytes(sourceFilePath);
		AddFile(fileName, fileContent);
	}

	public void AddFile(string fileName, byte[] fileData)
	{
		var originalFileName = Path.GetFileName(fileName);
		var originalExtension = Path.GetExtension(fileName) ?? "";

		var fm = new FileModel();
		var baseEncryptedFileId = GenerateId();
		fm.Name = originalFileName;
		fm.DateArchived = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		using (var sr = new MemoryStream(fileData))
		{
			byte[] bufferChunk;
			int chunkCount = 1;
			int bytesRead = 1;
			int bytesLeft = fileData.Length;
			while (true)
			{
				if (bytesLeft < 0) { break; }
				if (bytesLeft < MaxFileSizeBytes) { bufferChunk = new byte[bytesLeft]; } else { bufferChunk = new byte[MaxFileSizeBytes]; }
				var encryptedFileName = baseEncryptedFileId + "." + chunkCount + FileExtension + originalExtension;
				bytesRead = sr.Read(bufferChunk);
				if (bytesRead == 0) { break; }
				var encryptedPart = _encryptionService.Encrypt(bufferChunk, _password);
				File.WriteAllBytes(Path.Combine(_path, encryptedFileName), encryptedPart);
				fm.EncryptedFilePaths.Add(encryptedFileName);
				chunkCount++;
				bytesLeft -= bytesRead;
			}
		}
		_index.Files[baseEncryptedFileId] = fm;
		SaveChanges();
	}

	public void AddFolderContentsRecursively(string sourceFolderPath)
	{
		Console.WriteLine("Adding: " + sourceFolderPath);
		foreach (var filePath in Directory.GetFiles(sourceFolderPath))
		{
			AddFile(filePath);
		}

		foreach (var subDirectoryPath in Directory.GetDirectories(sourceFolderPath))
		{
			var subDirectoryName = new DirectoryInfo(subDirectoryPath).Name;
			var nestedVault = AddVault(subDirectoryName);
			nestedVault.AddFolderContentsRecursively(subDirectoryPath);
		}
	}

	// Get a file data
	public byte[] GetFile(string encryptedFileId)
	{
		if (_index.Files.TryGetValue(encryptedFileId, out var fm))
		{
			using (MemoryStream ms = new MemoryStream())
			{
				foreach (var encrptedFilePath in fm.EncryptedFilePaths)
				{
					var encryptedFilePath = Path.Combine(_path, encrptedFilePath);
					var encryptedContent = File.ReadAllBytes(encryptedFilePath);
					var decryptedPart = _encryptionService.Decrypt(encryptedContent, _password);
					ms.Seek(0, SeekOrigin.End);
					ms.Write(decryptedPart, 0, decryptedPart.Length);
				}
				return ms.ToArray();
			}
		}
		throw new IOException($"Encrypted Id '{encryptedFileId}' file not found/recognized in vault");
	}

	public void ExtractFile(string encryptedFileName, string destinationFolder)
	{
		var data = GetFile(encryptedFileName);
		var metadata = _index.Files[encryptedFileName];
		var fileDestination = Path.Join(destinationFolder, metadata.Name);
		if (!Directory.Exists(destinationFolder)) { throw new DirectoryNotFoundException(); }
		if (File.Exists(fileDestination)) { throw new IOException("File already exists!"); }
		File.WriteAllBytes(fileDestination, data);
	}

	// Add a vault
	public Vault AddVault(string vaultName, string? password = null)
	{
		var encryptedVaultName = GenerateId();
		while (_index.Vaults.ContainsKey(encryptedVaultName))
		{
			encryptedVaultName = GenerateId();
		}

		var nestedVaultPath = Path.Combine(_path, encryptedVaultName);
		var newVault = Vault.Create(nestedVaultPath, password ?? _password, _encryptionService);
		_index.Vaults[encryptedVaultName] = new();
		_index.Vaults[encryptedVaultName].Name = vaultName;
		SaveChanges();
		return newVault;
	}

	public Vault OpenVault(string encryptedVaultName, string? password = null)
	{
		if (_index.Vaults.TryGetValue(encryptedVaultName, out var vault))
		{
			var encryptedVaultPath = Path.Combine(_path, encryptedVaultName);
			var newVault = Vault.Open(encryptedVaultPath, password ?? _password, _encryptionService);
			return newVault;
		}
		throw new IOException($"Vault '{encryptedVaultName}' not found/recognized in vault");
	}

	public List<ReadOnlyDirItem> ListDir()
	{
		List<ReadOnlyDirItem> dirItems = [];
		foreach (var vault in _index.Vaults)
		{
			dirItems.Add(
				new ReadOnlyDirItem(
					vault.Key,
					vault.Value.Name,
					ReadOnlyDirItem.ItemType.Directory,
					new List<string>() { vault.Key },
					vault.Value.DateAdded
			));
		}
		foreach (var file in _index.Files)
		{
			dirItems.Add(
				new ReadOnlyDirItem(
					file.Key,
					file.Value.Name,
					ReadOnlyDirItem.ItemType.File,
					file.Value.EncryptedFilePaths,
					file.Value.DateArchived
			));
		}
		return dirItems;
	}
}

public record ReadOnlyDirItem
{
	public enum ItemType
	{
		Directory,
		File
	}

	public string EncryptedNameId { get; } = "";
	public string Name { get; } = "";
	public ItemType Type { get; } = ItemType.Directory;
	public IReadOnlyList<string> EncryptedNames { get; }
	public long DateArchived { get; } = 0;

	public ReadOnlyDirItem(
		string encryptedNameId,
		string name,
		ItemType type,
		List<string> encryptedNames,
		long dateArchived)
	{
		EncryptedNameId = encryptedNameId;
		Name = name;
		Type = type;
		EncryptedNames = encryptedNames;
		DateArchived = dateArchived;
	}
}