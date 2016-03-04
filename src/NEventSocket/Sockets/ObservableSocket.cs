﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="ObservableSocket.cs" company="Dan Barua">
//   (C) Dan Barua and contributors. Licensed under the Mozilla Public License.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace NEventSocket.Sockets
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Net.Sockets;
    using System.Reactive.Linq;
    using System.Reactive.PlatformServices;
    using System.Reactive.Subjects;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using NEventSocket.Logging;
    using NEventSocket.Util;
    using NEventSocket.Util.ObjectPooling;

    /// <summary>
    /// Wraps a <seealso cref="TcpClient"/> exposing incoming strings as an Observable sequence.
    /// </summary>
    public abstract class ObservableSocket : IDisposable
    {
        private static long IdCounter = 0;

        protected readonly long id;

        private readonly ILog Log;

        private readonly SemaphoreSlim syncLock = new SemaphoreSlim(1);

        private readonly InterlockedBoolean disposed = new InterlockedBoolean();

        private TcpClient tcpClient;

        private Subject<byte[]> subject;

        private IObservable<byte[]> receiver;
        
        private CancellationTokenSource cancellation;

        static ObservableSocket()
        {
            //we need this to work around issues ilmerging rx assemblies
            PlatformEnlightenmentProvider.Current = new CurrentPlatformEnlightenmentProvider();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ObservableSocket"/> class.
        /// </summary>
        /// <param name="tcpClient">The TCP client to wrap.</param>
        protected ObservableSocket(TcpClient tcpClient)
        {
            Log = LogProvider.GetLogger(GetType());

            this.id = Interlocked.Increment(ref IdCounter);

            this.tcpClient = tcpClient;

            subject = new Subject<byte[]>();

            cancellation = new CancellationTokenSource();

            receiver = Observable.Defer(
                () =>
                {
                    Task.Run(
                        async () =>
                        {
                            Log.Trace(() => "Observable Socket Worker Thread {0} started".Fmt(this.id));

                            int bytesRead = 1;
                            var stream = tcpClient.GetStream();
                            byte[] buffer = SharedPools.ByteArray.Allocate();
                            try
                            {
                                while (bytesRead > 0)
                                {
                                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellation.Token);
                                    if (bytesRead > 0)
                                    {
                                        if (bytesRead == buffer.Length)
                                        {
                                            this.subject.OnNext(buffer);
                                        }
                                        else
                                        {
                                            subject.OnNext(buffer.Take(bytesRead).ToArray());
                                        }
                                    }
                                    else
                                    {
                                        Dispose();
                                    }
                                }
                            }
                            catch (ObjectDisposedException)
                            {
                                //expected - normal shutdown
                                subject.OnCompleted();
                            }
                            catch (TaskCanceledException)
                            {
                                //expected - normal shutdown
                                subject.OnCompleted();
                            }
                            catch (SocketException ex)
                            {
                                //socket comms interrupted - propogate the error up the layers
                                Log.WarnException("Error reading from stream", ex);
                                subject.OnError(ex);
                            }
                            catch (IOException ex)
                            {
                                //socket comms interrupted - propogate the error up the layers
                                Log.WarnException("Error reading from stream", ex);
                                subject.OnError(ex);
                            }
                            catch (Exception ex)
                            {
                                //unexpected error
                                Log.ErrorException("Error reading from stream", ex);
                                subject.OnError(ex);
                            }
                            finally
                            {
                                SharedPools.ByteArray.Free(buffer);
                            }

                            Log.Trace(() => "Observable Socket Worker Thread {0} completed".Fmt(this.id));

                            Dispose();
                        });

                    return subject.AsObservable();
                });
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="ObservableSocket"/> class.
        /// </summary>
        ~ObservableSocket()
        {
            Dispose(false);
        }

        /// <summary>
        /// Occurs when the <see cref="ObservableSocket"/> is disposed.
        /// </summary>
        public event EventHandler Disposed = (sender, args) => { };

        /// <summary>
        /// Gets a value indicating whether this instance is connected.
        /// </summary>
        /// <value>
        /// <c>true</c> if this instance is connected; otherwise, <c>false</c>.
        /// </value>
        public bool IsConnected
        {
            get
            {
                return tcpClient != null && tcpClient.Connected;
            }
        }

        /// <summary>
        /// Gets an Observable sequence of byte array chunks as read from the socket stream.
        /// </summary>
        protected IObservable<byte[]> Receiver
        {
            get
            {
                return receiver;
            }
        }

        /// <summary>
        /// Asynchronously writes the given message to the socket.
        /// </summary>
        /// <param name="message">The string message to send</param>
        /// <param name="cancellationToken">A CancellationToken to cancel the send operation.</param>
        /// <returns>A Task.</returns>
        /// <exception cref="ObjectDisposedException">If disposed.</exception>
        /// <exception cref="InvalidOperationException">If not connected.</exception>
        public Task SendAsync(string message, CancellationToken cancellationToken)
        {
            return SendAsync(Encoding.ASCII.GetBytes(message), cancellationToken);
        }

        /// <summary>
        /// Asynchronously writes the given bytes to the socket.
        /// </summary>
        /// <param name="bytes">The raw byts to stream through the socket.</param>
        /// <param name="cancellationToken">A CancellationToken to cancel the send operation.</param>
        /// <returns>A Task.</returns>
        /// <exception cref="ObjectDisposedException">If disposed.</exception>
        /// <exception cref="InvalidOperationException">If not connected.</exception>
        public async Task SendAsync(byte[] bytes, CancellationToken cancellationToken)
        {
            if (disposed.Value)
            {
                throw new ObjectDisposedException(ToString());
            }

            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected");
            }

            try
            {
                await syncLock.WaitAsync().ConfigureAwait(false);
                var stream = GetStream();
                await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                Log.Warn(() => "Write operation was cancelled.");
                Dispose();
            }
            catch (IOException ex)
            {
                if (ex.InnerException is SocketException
                    && ((SocketException)ex.InnerException).SocketErrorCode == SocketError.ConnectionAborted)
                {
                    Log.Warn(() => "Socket disconnected");
                    Dispose();
                    return;
                }

                throw;
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.ConnectionAborted)
                {
                    Log.Warn(() => "Socket disconnected");
                    Dispose();
                    return;
                }

                throw;
            }
            catch (Exception ex)
            {
                Log.ErrorException("Error writing", ex);
                Dispose();
                throw;
            }
            finally
            {
                syncLock.Release();
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Gets the underlying network stream.
        /// </summary>
        protected virtual Stream GetStream()
        {
            return tcpClient.GetStream();
        }

        
        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed",
            MessageId = "received", 
            Justification = "received is disposed of asynchronously, when the buffer has been flushed out by the consumers")]
        protected virtual void Dispose(bool disposing)
        {
            if (!disposed.EnsureCalledOnce())
            {
                if (disposing)
                {
                    Log.Trace(() => "Disposing {0} (disposing:{1})".Fmt(GetType(), disposing));

                    if (cancellation.Token.CanBeCanceled)
                    {
                        cancellation.Cancel();
                    }

                    if (subject != null)
                    {
                        subject.Dispose();
                        subject = null;
                    }
                }

                if (IsConnected)
                {
                    if (tcpClient != null)
                    {
                        tcpClient.Close();
                        tcpClient = null;

                        if (Log != null) //could be running from finalizer
                        {
                            Log.Trace(() => "TcpClient closed");
                        }
                    }
                }

                var localCopy = Disposed;
                if (localCopy != null)
                {
                    localCopy(this, EventArgs.Empty);
                }

                if (Log != null) //could be running from finalizer
                {
                    Log.Debug(() => "{0} ({1}) Disposed".Fmt(GetType(), id));
                }
            }
        }
    }
}