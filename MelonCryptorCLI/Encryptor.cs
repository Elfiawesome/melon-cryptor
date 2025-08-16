using System.Security.Cryptography;
using System.Text;

public class EncryptService
{
	private byte[] _key;

	public EncryptService(string passwrod)
	{
		var sha256 = SHA256.Create();
		_key = sha256.ComputeHash(Encoding.UTF8.GetBytes(passwrod));
		Console.WriteLine($"Key generated successfully: {Convert.ToBase64String(_key)}");
	}

	public byte[] Encrypt(string data)
	{
		return Encrypt(Encoding.UTF8.GetBytes(data));
	}

	public byte[] Encrypt(byte[] data)
	{
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

	public byte[] Decrypt(byte[] encryptedData)
	{
		using (Aes aes = Aes.Create())
		{
			aes.Key = _key;

			var iv = new byte[aes.IV.Length];
			Array.Copy(encryptedData, 0, iv, 0, iv.Length);

			aes.IV = iv;

			using (var ms = new MemoryStream(encryptedData, iv.Length, encryptedData.Length - iv.Length))
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