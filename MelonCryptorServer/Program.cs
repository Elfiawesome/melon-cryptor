var webserver = new Webserver("http://localhost:8080/");

Console.CancelKeyPress += (sender, e) =>
{
	e.Cancel = true;
	webserver.Stop();
};

await webserver.Start();