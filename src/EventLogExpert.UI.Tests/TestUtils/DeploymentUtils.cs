// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Windows.Foundation;
using Windows.Management.Deployment;

namespace EventLogExpert.UI.Tests.TestUtils;

public static class DeploymentUtils
{
    /// <summary>A mock implementation of IAsyncOperationWithProgress for testing deployment callbacks.</summary>
    public sealed class MockDeploymentOperation : IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress>
    {
        private Exception? _errorCode;

        public AsyncOperationWithProgressCompletedHandler<DeploymentResult, DeploymentProgress>? Completed { get; set; }

        /// <summary>
        /// Returns the error that occurred during the async operation.
        /// Returns null when Status is not AsyncStatus.Error, matching real IAsyncInfo behavior.
        /// </summary>
        public Exception ErrorCode => _errorCode!;

        public uint Id => 0;

        public AsyncOperationProgressHandler<DeploymentResult, DeploymentProgress>? Progress { get; set; }

        public AsyncStatus Status { get; private set; } = AsyncStatus.Started;

        public void Cancel() => Status = AsyncStatus.Canceled;

        public void Close() { }

        public DeploymentResult GetResults() => throw new NotSupportedException("GetResults is not supported in mock.");

        public void SimulateCompleted(AsyncStatus status, Exception? error = null)
        {
            Status = status;
            _errorCode = status == AsyncStatus.Error ? error : null;
            Completed?.Invoke(this, status);
        }

        public void SimulateProgress(uint percentage)
        {
            Progress?.Invoke(this, new DeploymentProgress { percentage = percentage });
        }
    }
}
