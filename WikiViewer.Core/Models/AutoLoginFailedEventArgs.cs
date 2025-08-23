using System;

namespace WikiViewer.Core.Models
{
    public class AutoLoginFailedEventArgs : EventArgs
    {
        public WikiInstance Wiki { get; }
        public Exception Exception { get; }

        public AutoLoginFailedEventArgs(WikiInstance wiki, Exception exception)
        {
            Wiki = wiki;
            Exception = exception;
        }
    }
}