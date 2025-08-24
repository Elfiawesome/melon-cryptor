namespace MelonCryptor.Model;

public class IndexModel
{
	// Encrypted name -> File Name
	public Dictionary<string, FileModel> Files { get; set; } = [];

	// Encrypted name -> Vault Name
	public Dictionary<string, VaultModel> Vaults { get; set; } = [];
	public int IdCounter { get; set; } = 1;
}