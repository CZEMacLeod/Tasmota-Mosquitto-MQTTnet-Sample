using Humanizer;
using Microsoft.Extensions.Configuration;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Samples.Helpers;
using System.Buffers.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MQTT_Client;

internal class Program
{
	static async Task Main(string[] args)
	{
		// The server name can be passed in as an argument, or via usersecrets
		var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
			.AddUserSecrets<Program>()
			.AddCommandLine(args)
			.Build();

		var mqttFactory = new MqttFactory();
		using var mqttClient = mqttFactory.CreateMqttClient();
		var mqttClientOptions = new MqttClientOptionsBuilder().WithTcpServer(config["server"]).Build();

		// Setup message handling before connecting so that queued messages
		// are also handled properly. When there is no event handler attached all
		// received messages get lost.
		mqttClient.ApplicationMessageReceivedAsync += MqttClient_ApplicationMessageReceivedAsync;

		await mqttClient.ConnectAsync(mqttClientOptions, CancellationToken.None);

		await SubscribeAsync(mqttFactory, mqttClient, "$SYS/#");
		await SubscribeAsync(mqttFactory, mqttClient, "tele/#");

		Console.WriteLine("Press enter to exit.");
		Console.ReadLine();
	}

	// This regex matches telemetry topics produced by tasmota devices
	static readonly Regex topicRegex = new("^(?:tele/)(?<device>tasmota_[^/]+)(?:/)(?<infotype>[^/]+)$");

	private static Task MqttClient_ApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
	{
		string topic = e.ApplicationMessage.Topic;


		switch (topic)
		{
			case "$SYS/broker/publish/messages/received":
			case "$SYS/broker/publish/messages/sent":
			case "$SYS/broker/messages/sent":
			case "$SYS/broker/messages/received":
				HandleMessageCount(e, topic);
				break;
			case "$SYS/broker/publish/bytes/received":
			case "$SYS/broker/publish/bytes/sent":
			case "$SYS/broker/bytes/sent":
			case "$SYS/broker/bytes/received":
				HandleByteCount(e, topic);
				break;
			default:
				try
				{
					var result = topicRegex.Match(topic);
					if (result.Success)
					{
						HandleTasmota(e, result);
						break;
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex.Message);
				}

				// If the topic is unknown, then just use the extension method to show the payload as a string
				Console.WriteLine($"{topic}: {e.ApplicationMessage.ConvertPayloadToString()}");
				break;
		}
		return Task.CompletedTask;
	}

	private static void HandleByteCount(MqttApplicationMessageReceivedEventArgs e, string topic)
	{
		// This handles converting a numeric (long) payload in UTF8 format
		if (Utf8Parser.TryParse(e.ApplicationMessage.PayloadSegment, out long value, out var _))
		{
			var bytes = value.Bytes();
			// Output the topic and value (formatted nicely)
			Console.WriteLine($"{topic}: {bytes}");
		}
	}

	private static void HandleMessageCount(MqttApplicationMessageReceivedEventArgs e, string topic)
	{
		// This handles converting a numeric (ulong) payload in UTF8 format
		if (Utf8Parser.TryParse(e.ApplicationMessage.PayloadSegment, out ulong value, out var _))
		{
			// Output the topic and value (formatted nicely)
			Console.WriteLine($"{topic}: {value:N0} messages");
		}
	}

	private static void HandleTasmota(MqttApplicationMessageReceivedEventArgs e, Match result)
	{

		var dev = result.Groups["device"];
		var device = dev.Success ? dev.Value : null;
		switch (result.Groups["infotype"].Value)
		{
			case "LWT":
				// no enum method in Utf8Parser
				var payload = e.ApplicationMessage.ConvertPayloadToString();
				if (Enum.TryParse<Tasmota.LWT>(payload, out var lwt))
				{
					Console.WriteLine($"LWT for {device} is {lwt}");
				}
				break;
			case "STATE":
				HandleTasmotaState(e, device);
				break;
			default:
				Console.WriteLine($"{result.Value}: {e.ApplicationMessage.ConvertPayloadToString()}");
				break;
		}
	}

	private static void HandleTasmotaState(MqttApplicationMessageReceivedEventArgs e, string? device)
	{
		try
		{
			// Cast the payload as a readonlyspan<byte> so it can be consumed by System.Text.Json
			var utf8 = (ReadOnlySpan<byte>)e.ApplicationMessage.PayloadSegment;
			// Deserialize the payload directly
			var state = JsonSerializer.Deserialize<Tasmota.State>(utf8);
			if (state is not null)
			{
				// Use the state object to grab the source time and the extracted device name from the topic
				Console.Write($"{state.Time:g} {device} ");
				// Just dump the deserialized object back to the console for now
				state.DumpToConsole();
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine(ex.Message);
		}
	}

	private static async Task SubscribeAsync(MqttFactory mqttFactory, IMqttClient mqttClient, string topic)
	{
		var mqttSubscribeOptions = mqttFactory.CreateSubscribeOptionsBuilder()
			.WithTopicFilter(
				f =>
				{
					f.WithTopic(topic);
				})
			.Build();

		var result = await mqttClient.SubscribeAsync(mqttSubscribeOptions, CancellationToken.None);
		foreach (var tf in result.Items.Select(i => i.TopicFilter))
		{
			Console.WriteLine($"MQTT client subscribed to {tf.Topic}.");
		}
	}
}