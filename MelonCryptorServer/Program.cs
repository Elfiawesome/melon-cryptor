using MelonCryptorServer;

var prefix = "http://localhost:8080/";
// Assume wwwroot is in the same directory as the executable
var wwwrootPath = Path.Combine("C:/Users/elfia/OneDrive/Desktop/melon-cryptor/MelonCryptorServer", "wwwroot");

var server = new VaultHttpServer(prefix, wwwrootPath);

// Handle Ctrl+C to gracefully shut down the server
Console.CancelKeyPress += (sender, e) =>
{
	e.Cancel = true; // Prevent the process from terminating immediately
	server.Stop();
};

await server.Start();
