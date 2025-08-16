using MelonCryptorCLI;

string root = "c:/Users/elfia/OneDrive/Desktop/Melon-Cryptor/";

var mcs = new MelonCryptoService();

// mcs.NewVault($"{root}Asset-Testing/vault", "123");

mcs.OpenVault($"{root}Asset-Testing/vault", "123");

// Directory.GetFiles($"{root}Asset-Testing/source").ToList().ForEach(file =>
// {
// 	Console.WriteLine($"Stashing file: {file}");
// 	mcs.StashFile(file);
// });
