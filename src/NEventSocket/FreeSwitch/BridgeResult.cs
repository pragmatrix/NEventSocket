// --------------------------------------------------------------------------------------------------------------------
// <copyright file="BridgeResult.cs" company="Dan Barua">
//   (C) Dan Barua and contributors. Licensed under the Mozilla Public License.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace NEventSocket.FreeSwitch
{
    /// <summary>
    /// Represents the result of a Bridge attempt.
    /// </summary>
    public class BridgeResult : ApplicationResult
    {
        internal BridgeResult(EventMessage eventMessage) : base(eventMessage)
        {
            this.Success = eventMessage.Headers.ContainsKey(HeaderNames.OtherLegUniqueId);
            this.ResponseText = eventMessage.GetVariable("DIALSTATUS");

            if (this.Success)
            {
                this.BridgeUUID = eventMessage.Headers[HeaderNames.OtherLegUniqueId];
            }
        }

        /// <summary>
        /// Gets the UUID of the B-Leg Channel.
        /// </summary>
        public string BridgeUUID { get; private set; }
    }
}