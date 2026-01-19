using System.ComponentModel;

namespace Application.Types
{
    public enum AppStoredFileStatusTypes
    {
        [Description("Pending")]
        Pending = 1,

        [Description("Items in processing")]
        Processing = 2,

        [Description("Synced")]
        Complete = 3,

        [Description("Error during processing")]
        Error = 4,

        [Description("Path not founded")]
        PathNotFounded = 5,

        [Description("Locked files")]
        LockedFiles = 6
    }
}
