using System.Net;
using System.Text;
using System.Text.Json;
using MelonCryptorCLI; // Your service project

namespace MelonCryptorServer;

public class VaultHttpServer
{
	private readonly HttpListener _listener;
	private readonly MelonCryptorService _service;
	private readonly string _staticFilesPath;
	private bool _isRunning = false;

	public VaultHttpServer(string prefix, string staticFilesPath)
	{
		if (!HttpListener.IsSupported)
		{
			throw new NotSupportedException("HttpListener is not supported on this platform.");
		}

		_listener = new HttpListener();
		_listener.Prefixes.Add(prefix);
		_service = new MelonCryptorService(); // The server owns the service instance
		_staticFilesPath = staticFilesPath;
	}

	public async Task Start()
	{
		_listener.Start();
		Console.WriteLine($"Server started. Listening on {_listener.Prefixes.First()}");
		Console.WriteLine($"Serving static files from: {_staticFilesPath}");
		_isRunning = true;

		while (_isRunning)
		{
			try
			{
				var context = await _listener.GetContextAsync();
				// Don't block the listener loop. Handle each request on a thread pool thread.
				_ = Task.Run(() => HandleRequestAsync(context));
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
			string path = request.Url?.AbsolutePath ?? "/";
			Console.WriteLine($"{request.HttpMethod} {path}");

			// API Routing
			if (path.StartsWith("/api/"))
			{
				await HandleApiRequestAsync(context);
			}
			else // Static File Serving
			{
				await HandleStaticFileRequestAsync(context);
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[ERROR] {ex.Message}");
			await SendJsonResponseAsync(response, new { error = ex.Message }, HttpStatusCode.InternalServerError);
		}
		finally
		{
			response.OutputStream.Close();
		}
	}

	private async Task HandleApiRequestAsync(HttpListenerContext context)
	{
		var request = context.Request;
		var response = context.Response;
		var path = request.Url?.AbsolutePath;

		// Simple router
		switch (request.HttpMethod, path)
		{
			case ("GET", "/api/vault/status"):
				var status = _service.IsVaultOpen;
					// ? new { isOpen = true, path = _service.VaultPath, name = _service.Manifest?.VaultName }
					// : new { isOpen = false };
					await SendJsonResponseAsync(response, status);
				break;

			case ("POST", "/api/vault/open"):
				var openReq = await ReadJsonBodyAsync<OpenVaultRequest>(request);
				_service.OpenVault(openReq.Path, openReq.Password);
				await SendJsonResponseAsync(response, new { message = "Vault opened." });
				break;

			case ("POST", "/api/files/stash"):
				// NOTE: HttpListener doesn't have a built-in multipart/form-data parser.
				// This simplified API expects raw file bytes in the body and path/filename in query string.
				// The frontend JS will be adapted for this.
				var vaultPath = JsonSerializer.Deserialize<List<string>>(request.QueryString["path"] ?? "[]")!;
				var fileName = request.QueryString["filename"] ?? "unknown";

				var tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
				await using (var fs = new FileStream(tempFilePath, FileMode.Create))
				{
					await request.InputStream.CopyToAsync(fs);
				}

				// We need to rename the temp file to have the correct extension for the service.
				var finalTempPath = Path.ChangeExtension(tempFilePath, Path.GetExtension(fileName));
				File.Move(tempFilePath, finalTempPath);

				try
				{
					_service.StashFile(finalTempPath, vaultPath);
					await SendJsonResponseAsync(response, new { message = "File stashed." });
				}
				finally
				{
					if (File.Exists(finalTempPath)) File.Delete(finalTempPath);
				}
				break;

			case ("POST", "/api/files/retrieve"):
				var retrieveReq = await ReadJsonBodyAsync<PathRequest>(request);
				var fileBytes = _service.RetrieveFileBytes(retrieveReq.Path);
				response.ContentType = "application/octet-stream";
				response.AddHeader("Content-Disposition", $"attachment; filename=\"{retrieveReq.Path.Last()}\"");
				response.ContentLength64 = fileBytes.Length;
				await response.OutputStream.WriteAsync(fileBytes, 0, fileBytes.Length);
				break;

			// Add other API routes (delete, new vault, tree, etc.) here in the same pattern...

			default:
				await SendJsonResponseAsync(response, new { error = "Not Found" }, HttpStatusCode.NotFound);
				break;
		}
	}

	private async Task HandleStaticFileRequestAsync(HttpListenerContext context)
	{
		var request = context.Request;
		var response = context.Response;

		var path = request.Url?.AbsolutePath ?? "/";
		if (path == "/") path = "/index.html";

		// Basic security: prevent directory traversal attacks
		if (path.Contains(".."))
		{
			response.StatusCode = (int)HttpStatusCode.BadRequest;
			return;
		}

		var filePath = Path.Combine(_staticFilesPath, path.TrimStart('/'));

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

	// --- Helper Methods ---

	private async Task SendJsonResponseAsync(HttpListenerResponse response, object data, HttpStatusCode statusCode = HttpStatusCode.OK)
	{
		var json = JsonSerializer.Serialize(data);
		var buffer = Encoding.UTF8.GetBytes(json);
		response.ContentType = "application/json";
		response.StatusCode = (int)statusCode;
		response.ContentLength64 = buffer.Length;
		await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
	}

	private async Task<T> ReadJsonBodyAsync<T>(HttpListenerRequest request)
	{
		using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
		var body = await reader.ReadToEndAsync();
		return JsonSerializer.Deserialize<T>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
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

// Helper records for API requests (can be in the same file or a separate one)
public record OpenVaultRequest(string Path, string Password);
public record PathRequest(List<string> Path);