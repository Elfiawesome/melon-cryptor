using System.Text.Json;
using MelonCryptor.Encryption;
using MelonCryptor.Model;

namespace MelonCryptor;

public class Vault
{
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

	public void AddFile(string fileName, byte[] encryptedData)
	{
		var originalFileName = Path.GetFileName(fileName);
		var originalExtension = Path.GetExtension(fileName) ?? "";
		var encryptedFileName = GenerateId() + FileExtension + originalExtension;
		while (_index.Files.ContainsKey(encryptedFileName))
		{
			encryptedFileName = GenerateId() + FileExtension + originalExtension;
		}

		var encryptedContent = _encryptionService.Encrypt(encryptedData, _password);

		_index.Files[encryptedFileName] = new();
		_index.Files[encryptedFileName].Name = originalFileName;
		File.WriteAllBytes(Path.Combine(_path, encryptedFileName), encryptedContent);
		SaveChanges();
	}

	public void AddFolderContentsRecursively(string sourceFolderPath)
	{
		Console.WriteLine("Adding: "+sourceFolderPath);
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
	public byte[] GetFile(string encryptedFileName)
	{
		if (_index.Files.TryGetValue(encryptedFileName, out var file))
		{
			var encryptedFilePath = Path.Combine(_path, encryptedFileName);
			var encryptedContent = File.ReadAllBytes(encryptedFilePath);
			var decryptedContent = _encryptionService.Decrypt(encryptedContent, _password);
			return decryptedContent;
		}
		throw new IOException($"Encrypted '{encryptedFileName}' file not found/recognized in vault");
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
					ReadOnlyDirItem.ItemType.Directory,
					vault.Value.Name,
					vault.Key
			));
		}
		foreach (var vault in _index.Files)
		{
			dirItems.Add(
				new ReadOnlyDirItem(
					ReadOnlyDirItem.ItemType.File,
					vault.Value.Name,
					vault.Key
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

	public ItemType Type { get; } = ItemType.Directory;
	public string EncryptedName { get; } = "";
	public string Name { get; } = "";

	public ReadOnlyDirItem(ItemType type, string name, string encryptedName)
	{
		Type = type;
		EncryptedName = encryptedName;
		Name = name;
	}
}