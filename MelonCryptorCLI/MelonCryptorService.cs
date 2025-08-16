using System.Text;
using System.Text.Json;

namespace MelonCryptorCLI;

public class MelonCryptoService
{
	const string IndexFileName = "index.menc.json";
	public bool IsVaultOpen = false;
	public string? VaultPath;
	public IndexModel? Index => _index;
	private EncryptService? _encryptService;
	private IndexModel? _index;

	public void OpenVault(string vaultPath, string password)
	{
		if (!Directory.Exists(vaultPath))
			throw new DirectoryNotFoundException($"Vault directory not found: {vaultPath}");

		if (Directory.GetFiles(vaultPath).Length < 0)
			throw new IOException($"Target directory is empty: {vaultPath}");

		try
		{
			IsVaultOpen = true;
			VaultPath = vaultPath;
			_encryptService = new EncryptService(password);
			var encryptedIndexFile = ReadFileBytes(IndexFileName);
			var decryptedIndexFile = _encryptService.Decrypt(encryptedIndexFile);
			var indexModel = JsonSerializer.Deserialize<IndexModel>(decryptedIndexFile);
			if (indexModel == null || !indexModel.Success)
				throw new InvalidOperationException("Invalid password or index file.");

			_index = indexModel;
			Console.WriteLine($"Vault opened at: {vaultPath}");
		}
		catch (Exception ex)
		{
			IsVaultOpen = false;
			VaultPath = null;
			_encryptService = null;
			_index = null;
			throw new InvalidOperationException($"Failed to open vault at {vaultPath}. Ensure it is a valid MelonCryptor vault.", ex);
		}
	}

	public void NewVault(string vaultPath, string password)
	{
		if (Directory.GetFiles(vaultPath).Length > 0)
			throw new IOException($"Target directory is not empty: {vaultPath}");

		Directory.CreateDirectory(vaultPath);

		_index = new IndexModel() { Success = true };

		IsVaultOpen = true;
		VaultPath = vaultPath;
		_encryptService = new EncryptService(password);

		SaveIndex(_index);
		Console.WriteLine($"New vault created at: {vaultPath}");
	}

	public void StashFile(string absPath, List<string>? category = null)
	{
		CheckVaultStatus();

		if (!File.Exists(absPath))
			throw new FileNotFoundException($"Target File not found: {absPath}");


		if (_index == null)
			throw new ArgumentException("Index is null.");

		var newFileIndex = _index.FileIndexing++;

		// Encrypt file
		var data = File.ReadAllBytes(absPath);
		var encryptedData = _encryptService?.Encrypt(data);
		if (encryptedData == null)
			throw new InvalidOperationException("Received null encryption");

		// Store file
		var fileName = Path.GetFileName(absPath);
		var fileExt = Path.GetExtension(absPath);
		var destinationFileName = $"{newFileIndex}.menc{fileExt}";
		var destinationCategory = category != null && category.Count > 0 ? string.Join("_", category) : "default";
		var destinationPath = Path.Join(destinationCategory, destinationFileName);
		_index.Files[destinationPath] = new() { VaultPath = destinationPath, FileName = fileName };
		StoreFile(destinationPath, encryptedData);

		SaveIndex();
		Console.WriteLine($"File '{fileName}' stashed to vault at: {destinationFileName}");
	}

	public void RetrieveFile(string relPath, string targetPath)
	{
		CheckVaultStatus();

		if (File.Exists(targetPath))
			throw new FileNotFoundException($"Target File already exists: {targetPath}");

		var encryptedData = ReadFileBytes(relPath);
		var decryptedData = _encryptService?.Decrypt(encryptedData);

		if (decryptedData == null)
			throw new InvalidOperationException("Received null decryption");

		StoreFileGlobal(targetPath, decryptedData);
	}

	public void SaveIndex(IndexModel? index = null) // udpate as per now only
	{
		if (index == null)
			index = _index;
		if (index == null)
			throw new InvalidOperationException("Index is null.");

		var stringIndex = JsonSerializer.Serialize(index);
		var encryptedIndex = _encryptService?.Encrypt(str2bytes(stringIndex));
		if (encryptedIndex == null)
			throw new InvalidOperationException("Received null encryption for index.");
		StoreFile(IndexFileName, encryptedIndex);

		StoreFile("index.json", stringIndex); // Store unencrypted index for debugging
	}

	public void UpdateIndex() // Scan all to update the index (longer but safe)
	{

	}

	private byte[] ReadFileBytes(string path)
	{
		CheckVaultStatus();

		var targetPath = Path.Join(VaultPath, path);

		if (!File.Exists(targetPath))
			throw new FileNotFoundException($"File not found in vault: {targetPath}");

		return File.ReadAllBytes(targetPath);
	}

	private string ReadFileString(string path)
	{
		CheckVaultStatus();

		var targetPath = Path.Join(VaultPath, path);

		if (!File.Exists(targetPath))
			throw new FileNotFoundException($"File not found in vault: {targetPath}");

		return File.ReadAllText(targetPath);
	}

	private byte[] str2bytes(string data)
	{
		return Encoding.UTF8.GetBytes(data);
	}

	private string bytes2str(byte[] data)
	{
		return Encoding.UTF8.GetString(data);
	}

	private void StoreFile(string path, string data)
	{
		StoreFile(path, str2bytes(data));
	}

	private void StoreFile(string path, byte[] data)
	{
		CheckVaultStatus();

		var targetPath = Path.Join(VaultPath, path);
		var baseDir = Path.GetDirectoryName(targetPath);
		if (baseDir == null)
			throw new InvalidOperationException("Base directory is null.");

		if (!Directory.Exists(baseDir))
		{
			Directory.CreateDirectory(baseDir);
		}

		File.WriteAllBytes(targetPath, data);
	}

	private void StoreFileGlobal(string path, string data)
	{
		StoreFileGlobal(path, str2bytes(data));
	}

	private void StoreFileGlobal(string path, byte[] data)
	{
		File.WriteAllBytes(path, data);
	}

	private void CheckVaultStatus()
	{
		if (!IsVaultOpen)
			throw new InvalidOperationException("Vault is not opened or created.");
	}
}

public class IndexModel
{
	public bool Success { get; set; } = false;
	public string? VaultName { get; set; }
	public string? Description { get; set; }
	public string? Version { get; set; } = "0.1";
	public long FileIndexing { get; set; } = 1;

	public Dictionary<string, FileModel> Files { get; set; } = [];
}

public class FileModel
{
	public string VaultPath { get; set; } = "";
	public string FileName { get; set; } = "";
}