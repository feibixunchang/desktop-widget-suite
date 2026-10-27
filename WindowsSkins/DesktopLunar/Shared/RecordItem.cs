using System.Runtime.Serialization;

namespace Shared;

[DataContract]
internal sealed class RecordItem
{
	[DataMember]
	public string Id { get; set; }

	[DataMember]
	public string Date { get; set; }

	[DataMember]
	public string Type { get; set; }

	[DataMember]
	public string Category { get; set; }

	[DataMember]
	public double Amount { get; set; }

	[DataMember]
	public string Note { get; set; }

	[DataMember]
	public long CreatedAt { get; set; }
}
