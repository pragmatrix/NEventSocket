﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="PlayGetDigitsResult.cs" company="Dan Barua">
//   (C) Dan Barua and contributors. Licensed under the Mozilla Public License.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace NEventSocket.FreeSwitch
{
    /// <summary>
    /// Represents the result of the play_and_get_digits application
    /// </summary>
    public class PlayGetDigitsResult : ApplicationResult
    {
        internal PlayGetDigitsResult(EventMessage eventMessage, string channelVariable) : base(eventMessage)
        {
            this.Digits = eventMessage.GetVariable(channelVariable);

            this.TerminatorUsed = eventMessage.GetVariable("read_terminator_used");

            this.Success = !string.IsNullOrEmpty(this.Digits);
        }

        /// <summary>
        /// Gets the digits returned by the application
        /// </summary>
        public string Digits { get; private set; }

        /// <summary>
        /// Gets the terminating digit inputted by the user, if any
        /// </summary>
        public string TerminatorUsed { get; private set; }
    }
}