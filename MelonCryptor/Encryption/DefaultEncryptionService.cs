using System.Security.Cryptography;
using System.Text;

namespace MelonCryptor.Encryption;

public class DefaultEncryptionService : IEncryptionService
{
	private byte[] ConvertPasswordToKey(string password)
	{
		using (var sha256 = SHA256.Create())
		{
			return sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
		}
	}

	public byte[] Encrypt(byte[] data, string password)
	{
		var _key = ConvertPasswordToKey(password);
		using (Aes aes = Aes.Create())
		{
			aes.Key = _key;
			aes.GenerateIV();

			var ms = new MemoryStream();
			ms.Write(aes.IV, 0, aes.IV.Length);

			using (var encryptor = aes.CreateEncryptor())
			{
				using (var csEncrypt = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
				{
					csEncrypt.Write(data, 0, data.Length);
					csEncrypt.FlushFinalBlock();
					return ms.ToArray();
				}
			}
		}
	}

	public byte[] Decrypt(byte[] data, string password)
	{
		var _key = ConvertPasswordToKey(password);
		using (Aes aes = Aes.Create())
		{
			aes.Key = _key;

			var iv = new byte[aes.IV.Length];
			Array.Copy(data, 0, iv, 0, iv.Length);

			aes.IV = iv;

			using (var ms = new MemoryStream(data, iv.Length, data.Length - iv.Length))
			{
				using (var decryptor = aes.CreateDecryptor())
				{
					using (var csDecrypt = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
					{
						using (var resultStream = new MemoryStream())
						{
							csDecrypt.CopyTo(resultStream);
							return resultStream.ToArray();
						}
					}
				}
			}
		}
	}
}