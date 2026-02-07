namespace UniCortex
{
    /// <summary>
    /// UniCortex の全操作で使用するエラーコード。
    /// </summary>
    public enum ErrorCode : byte
    {
        None = 0,
        NotFound,
        DuplicateId,
        CapacityExceeded,
        DimensionMismatch,
        InvalidParameter,
        IndexNotBuilt,
        FileNotFound,
        InvalidFileFormat,
        IncompatibleVersion,
        DataCorrupted,
        IoError,
        StorageQuotaExceeded
    }

    /// <summary>
    /// エラーコード付きの結果型。Burst 互換のため unmanaged 制約。
    /// </summary>
    /// <typeparam name="T">結果の値型</typeparam>
    public struct Result<T> where T : unmanaged
    {
        public ErrorCode Error;
        public T Value;

        public bool IsSuccess => Error == ErrorCode.None;

        public static Result<T> Success(T value)
        {
            return new Result<T> { Error = ErrorCode.None, Value = value };
        }

        public static Result<T> Fail(ErrorCode error)
        {
            return new Result<T> { Error = error, Value = default };
        }
    }
}
