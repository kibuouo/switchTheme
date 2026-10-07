using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace SwitchTheme
{
    [DataContract]
    public sealed class ResumeState
    {
        [DataMember] public Settings Draft;
        [DataMember] public DateTimeOffset? PausedUntil;
        public string Encode()
        {
            using (var stream = new MemoryStream())
            { new DataContractJsonSerializer(typeof(ResumeState)).WriteObject(stream, this); return Convert.ToBase64String(stream.ToArray()); }
        }
        public static ResumeState Decode(string encoded)
        {
            if (encoded == null || encoded.Length > 4096) throw new ArgumentException("后台交接参数无效。");
            using (var stream = new MemoryStream(Convert.FromBase64String(encoded)))
                return (ResumeState)new DataContractJsonSerializer(typeof(ResumeState)).ReadObject(stream);
        }
    }
}
