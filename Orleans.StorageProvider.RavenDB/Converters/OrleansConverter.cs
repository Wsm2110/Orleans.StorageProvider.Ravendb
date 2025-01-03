using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Orleans.StorageProvider.RavenDB.Converters;

public static class OrleansConverters
{
    public class MembershipEntryConverter : JsonConverter<MembershipEntry>
    {
        public override MembershipEntry ReadJson(JsonReader reader, Type objectType, MembershipEntry existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var jsonObject = JObject.Load(reader);
            var membershipEntry = new MembershipEntry();

            if (jsonObject.TryGetValue("SiloAddress", out JToken siloAddressToken))
            {
                membershipEntry.SiloAddress = siloAddressToken.ToObject<SiloAddress>(serializer);
            }

            if (jsonObject.TryGetValue("Status", out JToken statusToken))
            {
                membershipEntry.Status = statusToken.ToObject<SiloStatus>(serializer);
            }

            if (jsonObject.TryGetValue("SuspectTimes", out JToken suspectTimesToken))
            {
                membershipEntry.SuspectTimes = suspectTimesToken.ToObject<List<Tuple<SiloAddress, DateTime>>>(serializer);
            }

            if (jsonObject.TryGetValue("ProxyPort", out JToken proxyPortToken))
            {
                membershipEntry.ProxyPort = proxyPortToken.Value<int>();
            }

            if (jsonObject.TryGetValue("HostName", out JToken hostNameToken))
            {
                membershipEntry.HostName = hostNameToken.Value<string>();
            }

            if (jsonObject.TryGetValue("SiloName", out JToken siloNameToken))
            {
                membershipEntry.SiloName = siloNameToken.Value<string>();
            }

            if (jsonObject.TryGetValue("RoleName", out JToken roleNameToken))
            {
                membershipEntry.RoleName = roleNameToken.Value<string>();
            }

            if (jsonObject.TryGetValue("UpdateZone", out JToken updateZoneToken))
            {
                membershipEntry.UpdateZone = updateZoneToken.Value<int>();
            }

            if (jsonObject.TryGetValue("FaultZone", out JToken faultZoneToken))
            {
                membershipEntry.FaultZone = faultZoneToken.Value<int>();
            }

            if (jsonObject.TryGetValue("StartTime", out JToken startTimeToken))
            {
                membershipEntry.StartTime = startTimeToken.Value<DateTime>();
            }

            if (jsonObject.TryGetValue("IAmAliveTime", out JToken iAmAliveTimeToken))
            {
                membershipEntry.IAmAliveTime = iAmAliveTimeToken.Value<DateTime>();
            }

            return membershipEntry;
        }

        public override void WriteJson(JsonWriter writer, MembershipEntry value, JsonSerializer serializer)
        {
            var jsonObject = new JObject();

            jsonObject.Add("SiloAddress", JToken.FromObject(value.SiloAddress, serializer));
            jsonObject.Add("Status", JToken.FromObject(value.Status, serializer));
            jsonObject.Add("SuspectTimes", JToken.FromObject(value.SuspectTimes, serializer));
            jsonObject.Add("ProxyPort", value.ProxyPort);
            jsonObject.Add("HostName", value.HostName);
            jsonObject.Add("SiloName", value.SiloName);
            jsonObject.Add("RoleName", value.RoleName);
            jsonObject.Add("UpdateZone", value.UpdateZone);
            jsonObject.Add("FaultZone", value.FaultZone);
            jsonObject.Add("StartTime", value.StartTime);
            jsonObject.Add("IAmAliveTime", value.IAmAliveTime);

            jsonObject.WriteTo(writer);
        }
    }

    public class SiloAddressConverter : JsonConverter<SiloAddress>
    {
        public override SiloAddress ReadJson(JsonReader reader, Type objectType, SiloAddress existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var value = reader.Value?.ToString();
            if (string.IsNullOrEmpty(value))
            {
                return null!;
            }

            // Deserialize from string representation
            return SiloAddress.FromParsableString(value);
        }

        public override void WriteJson(JsonWriter writer, SiloAddress value, JsonSerializer serializer)
        {
            // Serialize to string representation
            writer.WriteValue(value.ToParsableString());
        }
    }

    public class IPAddressConverter : JsonConverter<IPAddress>
    {
        public override IPAddress ReadJson(JsonReader reader, Type objectType, IPAddress? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var value = reader.Value?.ToString();
            if (string.IsNullOrEmpty(value))
            {
                return null!;
            }

            // Deserialize IPAddress from string
            return IPAddress.Parse(value);
        }

        public override void WriteJson(JsonWriter writer, IPAddress? value, JsonSerializer serializer)
        {
            // Serialize IPAddress to string
            writer.WriteValue(value?.ToString());
        }
    }
}