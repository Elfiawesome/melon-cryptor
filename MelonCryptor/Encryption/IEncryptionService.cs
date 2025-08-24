using System.Text;

namespace MelonCryptor.Encryption;

public interface IEncryptionService
{
	public byte[] Encrypt(byte[] data, string password);
	public byte[] Encrypt(string plainText, string password)
	{
		return Encrypt(Encoding.UTF8.GetBytes(plainText), password);
	}

	public byte[] Decrypt(byte[] data, string password);
}