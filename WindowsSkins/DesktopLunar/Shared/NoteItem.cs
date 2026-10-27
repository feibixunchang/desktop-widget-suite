using System.Runtime.Serialization;

namespace Shared;

[DataContract]
internal sealed class NoteItem
{
	[DataMember]
	public string Id { get; set; }

	[DataMember]
	public string Date { get; set; }

	[DataMember]
	public string Time { get; set; }

	[DataMember]
	public string Content { get; set; }

	[DataMember]
	public string Color { get; set; }

	[DataMember]
	public long CreatedAt { get; set; }
}
