using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.Xaml.Data;

namespace WikiViewer.Shared.Uwp.Converters
{
    public class StringUrlToImageSourceConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string imageUrl && !string.IsNullOrEmpty(imageUrl))
            {
                return new NotifyTaskCompletion<string>(GetBase64DataUriFromFileAsync(imageUrl));
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }

        private async Task<string> GetBase64DataUriFromFileAsync(string imageUrl)
        {
            try
            {
                var uri = new Uri(imageUrl);
                StorageFile file = await StorageFile.GetFileFromApplicationUriAsync(uri);
                var buffer = await FileIO.ReadBufferAsync(file);

                string base64 = System.Convert.ToBase64String(buffer.ToArray());

                string mimeType = "image/png";
                string extension = Path.GetExtension(imageUrl).ToLowerInvariant();
                if (extension == ".jpg" || extension == ".jpeg")
                    mimeType = "image/jpeg";
                else if (extension == ".gif")
                    mimeType = "image/gif";
                else if (extension == ".svg")
                    mimeType = "image/svg+xml";

                return $"data:{mimeType};base64,{base64}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Base64Converter] Failed for {imageUrl}: {ex.Message}"
                );
                return null;
            }
        }
    }

    public sealed class NotifyTaskCompletion<TResult> : System.ComponentModel.INotifyPropertyChanged
    {
        public NotifyTaskCompletion(Task<TResult> task)
        {
            Task = task;
            if (!task.IsCompleted)
            {
                var _ = WatchTaskAsync(task);
            }
        }

        private async Task WatchTaskAsync(Task task)
        {
            try
            {
                await task;
            }
            catch { }

            var propertyChanged = PropertyChanged;
            if (propertyChanged == null)
                return;

            propertyChanged(
                this,
                new System.ComponentModel.PropertyChangedEventArgs(nameof(Status))
            );
            propertyChanged(
                this,
                new System.ComponentModel.PropertyChangedEventArgs(nameof(IsCompleted))
            );
            propertyChanged(
                this,
                new System.ComponentModel.PropertyChangedEventArgs(nameof(IsNotCompleted))
            );
            if (task.IsCanceled)
            {
                propertyChanged(
                    this,
                    new System.ComponentModel.PropertyChangedEventArgs(nameof(IsCanceled))
                );
            }
            else if (task.IsFaulted)
            {
                propertyChanged(
                    this,
                    new System.ComponentModel.PropertyChangedEventArgs(nameof(IsFaulted))
                );
                propertyChanged(
                    this,
                    new System.ComponentModel.PropertyChangedEventArgs(nameof(Exception))
                );
                propertyChanged(
                    this,
                    new System.ComponentModel.PropertyChangedEventArgs(nameof(InnerException))
                );
                propertyChanged(
                    this,
                    new System.ComponentModel.PropertyChangedEventArgs(nameof(ErrorMessage))
                );
            }
            else
            {
                propertyChanged(
                    this,
                    new System.ComponentModel.PropertyChangedEventArgs(
                        nameof(IsSuccessfullyCompleted)
                    )
                );
                propertyChanged(
                    this,
                    new System.ComponentModel.PropertyChangedEventArgs(nameof(Result))
                );
            }
        }

        public Task<TResult> Task { get; }
        public TResult Result =>
            (Task.Status == TaskStatus.RanToCompletion) ? Task.Result : default(TResult);
        public TaskStatus Status => Task.Status;
        public bool IsCompleted => Task.IsCompleted;
        public bool IsNotCompleted => !Task.IsCompleted;
        public bool IsSuccessfullyCompleted => Task.Status == TaskStatus.RanToCompletion;
        public bool IsCanceled => Task.IsCanceled;
        public bool IsFaulted => Task.IsFaulted;
        public AggregateException Exception => Task.Exception;
        public Exception InnerException => Exception?.InnerException;
        public string ErrorMessage => InnerException?.Message;
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }
}
