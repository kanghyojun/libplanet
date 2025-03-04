using System.Numerics;

namespace Libplanet.Net
{
    /// <summary>
    /// Indicates a progress of downloading state references.
    /// </summary>
    public class StateReferenceDownloadState : PreloadState
    {
        /// <summary>
        /// Total number of state references to receive in the current batch.
        /// </summary>
        public int TotalStateReferenceCount { get; internal set; }

        /// <summary>
        /// The number of received state references until now.
        /// </summary>
        public int ReceivedStateReferenceCount { get; internal set; }

        /// <summary>
        /// The address of the state references just received.
        /// </summary>
        public Address ReceivedAddress { get; internal set; }

        /// <inheritdoc />
        public override int CurrentPhase => 2;
    }
}
