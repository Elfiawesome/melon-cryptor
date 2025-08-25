namespace MelonCryptor.Model;

public class FileModel
{
	public string Name { get; set; } = "";
	public string Description { get; set; } = "";
	public long DateArchived { get; set; } = 0;
	public List<string> EncryptedFilePaths { get; set; } = [];
}