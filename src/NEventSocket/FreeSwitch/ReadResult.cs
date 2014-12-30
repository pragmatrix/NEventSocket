﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ReadResult.cs" company="Dan Barua">
//   (C) Dan Barua and contributors. Licensed under the Mozilla Public License.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
namespace NEventSocket.FreeSwitch
{
    using System;

    /// <summary>
    /// Represents the result of the Read dialplan application
    /// </summary>
    public class ReadResult : ApplicationResult
    {
        internal ReadResult(EventMessage eventMessage, string channelVariable) : base(eventMessage)
        {
            this.Digits = eventMessage.GetVariable(channelVariable);
            var readResult = eventMessage.GetVariable("read_result");
            this.Result = !string.IsNullOrEmpty(readResult) ? (ReadResultStatus)Enum.Parse(typeof(ReadResultStatus), readResult, true) : ReadResultStatus.Failure;
        }

        /// <summary>
        /// Gets a string indicating the status of the read operation, "success", "timeout" or "failure"
        /// </summary>
        public ReadResultStatus Result { get; private set; }

        /// <summary>
        /// Gets the digits read from the Channel.
        /// </summary>
        public string Digits { get; private set; }
    }
}