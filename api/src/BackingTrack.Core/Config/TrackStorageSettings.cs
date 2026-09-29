using System.ComponentModel.DataAnnotations;

namespace BackingTrack.Core.Config;

public class TrackStorageSettings
{
    [Required]
    public required string BaseFolderPath { get; set; }
}