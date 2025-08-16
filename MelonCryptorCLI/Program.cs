using MelonCryptorCLI;

try
{
	string root = "c:/Users/elfia/OneDrive/Desktop/Melon-Cryptor/";

	var mcs = new MelonCryptoService();

	// mcs.NewVault($"{root}Asset-Testing/vault-debug", "supersecretpassword999");

	mcs.OpenVault($"{root}Asset-Testing/vault-debug", "supersecretpassword999");

	// Directory.GetFiles($"{root}Asset-Testing/source").ToList().ForEach(file =>
	// {
	// 	Console.WriteLine($"Stashing file: {file}");
	// 	mcs.StashFile(file);
	// });

	mcs.StashFile("c:/Users/elfia/OneDrive/Desktop/Launcher.exe", ["apps", "custom", "launchers"]);
}
catch (Exception ex)
{
	Console.WriteLine($"Error: {ex.Message}");
}
