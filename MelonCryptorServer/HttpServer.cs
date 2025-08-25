using System.Net;
using System.Text;
using System.Text.Json;
using MelonCryptor;
using MelonCryptor.Encryption;

public class Webserver
{
	private readonly HttpListener _listener;
	private bool _isRunning = false;

	public Webserver(string prefix)
	{
		_listener = new HttpListener();
		_listener.Prefixes.Add(prefix);
	}

	public async Task Start()
	{
		_listener.Start();
		Console.WriteLine($"Server started. Listening on {_listener.Prefixes.First()}");
		_isRunning = true;
		while (_isRunning)
		{
			try
			{
				var context = await _listener.GetContextAsync();
				await HandleRequestAsync(context);
			}
			catch (HttpListenerException) when (!_isRunning)
			{
				// Listener was stopped, this is expected.
				break;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Request handling error: {ex.Message}");
			}
		}
	}

	public void Stop()
	{
		_isRunning = false;
		_listener.Stop();
		_listener.Close();
		Console.WriteLine("Server stopped.");
	}

	private async Task HandleRequestAsync(HttpListenerContext context)
	{
		var request = context.Request;
		var response = context.Response;
		try
		{
			var path = request.Url?.AbsolutePath ?? "/";
			Console.WriteLine($"{request.HttpMethod} {path}");

			if (path.StartsWith("/api/"))
			{
				await HandleApiRequestAsync(context);
			}
			else
			{
				await HandleStaticFileRequestAsync(context);
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[ERROR] {ex.Message}");
			await SendJsonResponseAsync(response, new { error = ex.Message, stacktrace = ex.StackTrace }, HttpStatusCode.InternalServerError);
		}
		finally
		{
			response.OutputStream.Close();
		}
	}

	private async Task HandleStaticFileRequestAsync(HttpListenerContext context)
	{
		var request = context.Request;
		var response = context.Response;
		var path = request.Url?.AbsolutePath ?? "/";
		if (path == "/") { path = "/index.html"; }

		if (path.Contains(".."))
		{
			response.StatusCode = (int)HttpStatusCode.BadRequest;
			return;
		}

		var filePath = Path.Combine("C:\\Users\\elfia\\OneDrive\\Desktop\\melon-cryptor\\MelonCryptorServer\\Asset\\", path.TrimStart('/'));
		if (File.Exists(filePath))
		{
			response.ContentType = GetMimeType(filePath);
			var buffer = await File.ReadAllBytesAsync(filePath);
			response.ContentLength64 = buffer.Length;
			await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
		}
		else
		{
			response.StatusCode = (int)HttpStatusCode.NotFound;
			await SendJsonResponseAsync(response, new { error = "File not found" }, HttpStatusCode.NotFound);
		}
	}

	private async Task HandleApiRequestAsync(HttpListenerContext context)
	{
		var request = context.Request;
		var response = context.Response;
		var path = request.Url?.AbsolutePath;

		string vaultPath;
		string password;
		Vault v;

		// Simple router
		switch (request.HttpMethod, path)
		{
			case ("GET", "/api/get-vault"):
				vaultPath = (request.QueryString.GetValues("vault") ?? throw new IOException("Invalid API Request"))[0];
				password = (request.QueryString.GetValues("password") ?? throw new IOException("Invalid API Request"))[0];
				v = Vault.Open(vaultPath, password, new DefaultEncryptionService());
				var dir = v.ListDir();
				await SendJsonResponseAsync(response, dir);
				break;
			case ("POST", "/api/create-vault"):
				vaultPath = (request.QueryString.GetValues("vault") ?? throw new IOException("Invalid API Request"))[0];
				password = (request.QueryString.GetValues("password") ?? throw new IOException("Invalid API Request"))[0];
				v = Vault.Create(vaultPath, password, new DefaultEncryptionService());
				await SendJsonResponseAsync(response, new { });
				break;
			case ("GET", "/api/get-vault-file"):
				vaultPath = (request.QueryString.GetValues("vault") ?? throw new IOException("Invalid API Request"))[0];
				password = (request.QueryString.GetValues("password") ?? throw new IOException("Invalid API Request"))[0];
				var encryptedFileId = (request.QueryString.GetValues("encrypted-file-id") ?? throw new IOException("Invalid API Request"))[0];
				v = Vault.Open(vaultPath, password, new DefaultEncryptionService());
				var data = v.GetFile(encryptedFileId);
				await SendRawResponseAsync(response, data);
				break;
			case ("POST", "/api/upload-file"):
				vaultPath = (request.QueryString.GetValues("vault") ?? throw new IOException("Invalid API Request"))[0];
				password = (request.QueryString.GetValues("password") ?? throw new IOException("Invalid API Request"))[0];
				v = Vault.Open(vaultPath, password, new DefaultEncryptionService());
				using (var memoryStream = new MemoryStream())
				{
					await request.InputStream.CopyToAsync(memoryStream);
					byte[] fileBytes = memoryStream.ToArray();
					var fileName = request.Headers["filename"] ?? "untitled";
					Console.WriteLine($"Uploading {fileName}");
					v.AddFile(fileName, fileBytes);
				}
				await SendJsonResponseAsync(response, new { });
				break;
			case ("POST", "/api/add-vault"):
				vaultPath = (request.QueryString.GetValues("vault") ?? throw new IOException("Invalid API Request"))[0];
				password = (request.QueryString.GetValues("password") ?? throw new IOException("Invalid API Request"))[0];
				string vaultName = (request.QueryString.GetValues("name") ?? throw new IOException("Invalid API Request"))[0];
				v = Vault.Open(vaultPath, password, new DefaultEncryptionService());
				v.AddVault(vaultName);
				await SendJsonResponseAsync(response, new { });
				break;


		}
	}

	private async Task SendJsonResponseAsync<T>(HttpListenerResponse response, T data, HttpStatusCode statusCode = HttpStatusCode.OK)
		where T : notnull
	{
		var serializeOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, };
		var json = JsonSerializer.Serialize(data, serializeOptions);
		var buffer = Encoding.UTF8.GetBytes(json);
		response.ContentType = "application/json";
		await SendRawResponseAsync(response, buffer, statusCode);
	}

	private async Task SendRawResponseAsync(HttpListenerResponse response, byte[] data, HttpStatusCode statusCode = HttpStatusCode.OK)
	{
		response.StatusCode = (int)statusCode;
		response.ContentLength64 = data.Length;
		await response.OutputStream.WriteAsync(data, 0, data.Length);
	}

	private string GetMimeType(string fileName)
	{
		return Path.GetExtension(fileName).ToLowerInvariant() switch
		{
			".html" => "text/html",
			".js" => "application/javascript",
			".css" => "text/css",
			".json" => "application/json",
			".png" => "image/png",
			".jpg" => "image/jpeg",
			_ => "application/octet-stream",
		};
	}
}