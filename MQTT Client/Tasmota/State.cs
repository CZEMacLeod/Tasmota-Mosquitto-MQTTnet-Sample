using System.Text.Json;
using System.Text.Json.Serialization;

namespace MQTT_Client.Tasmota
{
	public class TimeSpanConverter : JsonConverter<TimeSpan>
	{
		private const string format = "d\\Thh\\:mm\\:ss";
		public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			var s = reader.GetString();
			return s is null ? 
				throw new NullReferenceException("Timespan value was null") : 
				TimeSpan.ParseExact(s, format, null);
		}

		public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options) =>
			writer.WriteStringValue(value.ToString(format));
	}

	public class State
	{
		public DateTime Time { get; set; }

		[JsonConverter(typeof(TimeSpanConverter))]
		public TimeSpan Uptime { get; set; }

		public ulong UptimeSec { get; set; }
		public int Heap { get; set; }
		public string? SleepMode { get; set; }
		public int Sleep { get; set; }
		public int LoadAvg { get; set; }
		public int MqttCount { get; set; }
		public BerryState Berry { get; set; }

	}
}
