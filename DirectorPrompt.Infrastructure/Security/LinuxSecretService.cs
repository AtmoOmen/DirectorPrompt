using System.Runtime.InteropServices;
using DirectorPrompt.Domain.Services;

namespace DirectorPrompt.Infrastructure.Security;

internal sealed class LinuxSecretService : ISecretStore
{
    private const string GLIB_LIBRARY = "libglib-2.0.so.0";
    private const string SECRET_LIBRARY = "libsecret-1.so.0";

    private static readonly Lazy<(nint StringHash, nint StringEqual)> StringFunctions = new(LoadStringFunctions);

    public string? Get(string key) =>
        Execute
        (() =>
            {
                using var attributes = new AttributeTable(key);
                var password = SecretPasswordLookup(0, attributes.Handle, 0, out var error);
                ThrowIfError(error, "读取");

                if (password == 0)
                    return null;

                try
                {
                    return Marshal.PtrToStringUTF8(password);
                }
                finally
                {
                    SecretPasswordFree(password);
                }
            }
        );

    public void Set(string key, string value) =>
        Execute
        (() =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(key);
                ArgumentNullException.ThrowIfNull(value);

                using var attributes = new AttributeTable(key);
                var stored = SecretPasswordStore
                (
                    0,
                    attributes.Handle,
                    0,
                    "DirectorPrompt credential",
                    value,
                    0,
                    out var error
                );
                ThrowIfError(error, "写入");

                if (!stored)
                    throw new InvalidOperationException("Linux Secret Service 未保存密钥");
            }
        );

    public void Remove(string key) =>
        Execute
        (() =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(key);

                using var attributes = new AttributeTable(key);
                _ = SecretPasswordClear(0, attributes.Handle, 0, out var error);
                ThrowIfError(error, "删除");
            }
        );

    private static T Execute<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (DllNotFoundException exception)
        {
            throw new PlatformNotSupportedException("Linux Secret Service 不可用, 请安装 libsecret-1 并启用桌面密钥环", exception);
        }
        catch (EntryPointNotFoundException exception)
        {
            throw new PlatformNotSupportedException("当前 Linux 系统的 libsecret 版本不支持安全凭据存储", exception);
        }
    }

    private static void Execute(Action action) =>
        Execute
        (() =>
            {
                action();
                return true;
            }
        );

    private static (nint StringHash, nint StringEqual) LoadStringFunctions()
    {
        if (!NativeLibrary.TryLoad(GLIB_LIBRARY, out var library))
            throw new PlatformNotSupportedException("Linux Secret Service 依赖的 GLib 不可用");

        return
        (
            NativeLibrary.GetExport(library, "g_str_hash"),
            NativeLibrary.GetExport(library, "g_str_equal")
        );
    }

    private static void ThrowIfError(nint error, string operation)
    {
        if (error == 0)
            return;

        try
        {
            var nativeError = Marshal.PtrToStructure<GError>(error);
            var message = Marshal.PtrToStringUTF8(nativeError.Message) ?? "未知错误";

            throw new InvalidOperationException($"Linux Secret Service {operation}失败: {message}");
        }
        finally
        {
            GErrorFree(error);
        }
    }

    [DllImport(SECRET_LIBRARY, CallingConvention = CallingConvention.Cdecl, EntryPoint = "secret_password_lookupv_sync")]
    private static extern nint SecretPasswordLookup
    (
        nint schema,
        nint attributes,
        nint cancellable,
        out nint error
    );

    [DllImport(SECRET_LIBRARY, CallingConvention = CallingConvention.Cdecl, EntryPoint = "secret_password_storev_sync")]
    [return: MarshalAs(UnmanagedType.I4)]
    private static extern bool SecretPasswordStore
    (
        nint schema,
        nint attributes,
        nint collection,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string label,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string password,
        nint cancellable,
        out nint error
    );

    [DllImport(SECRET_LIBRARY, CallingConvention = CallingConvention.Cdecl, EntryPoint = "secret_password_clearv_sync")]
    [return: MarshalAs(UnmanagedType.I4)]
    private static extern bool SecretPasswordClear
    (
        nint schema,
        nint attributes,
        nint cancellable,
        out nint error
    );

    [DllImport(SECRET_LIBRARY, CallingConvention = CallingConvention.Cdecl, EntryPoint = "secret_password_free")]
    private static extern void SecretPasswordFree(nint password);

    [DllImport(GLIB_LIBRARY, CallingConvention = CallingConvention.Cdecl, EntryPoint = "g_error_free")]
    private static extern void GErrorFree(nint error);

    [DllImport(GLIB_LIBRARY, CallingConvention = CallingConvention.Cdecl, EntryPoint = "g_hash_table_new")]
    private static extern nint GHashTableNew(nint hashFunction, nint equalFunction);

    [DllImport(GLIB_LIBRARY, CallingConvention = CallingConvention.Cdecl, EntryPoint = "g_hash_table_insert")]
    private static extern void GHashTableInsert(nint table, nint key, nint value);

    [DllImport(GLIB_LIBRARY, CallingConvention = CallingConvention.Cdecl, EntryPoint = "g_hash_table_destroy")]
    private static extern void GHashTableDestroy(nint table);

    [StructLayout(LayoutKind.Sequential)]
    private struct GError
    {
        public uint Domain;

        public int Code;

        public nint Message;
    }

    private sealed class AttributeTable : IDisposable
    {
        private readonly nint applicationKey;
        private readonly nint applicationValue;
        private readonly nint secretKey;
        private readonly nint secretValue;

        public nint Handle { get; }

        public AttributeTable(string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            var functions = StringFunctions.Value;
            Handle = GHashTableNew(functions.StringHash, functions.StringEqual);

            if (Handle == 0)
                throw new InvalidOperationException("无法创建 Linux Secret Service 属性表");

            try
            {
                applicationKey   = Marshal.StringToCoTaskMemUTF8("application");
                applicationValue = Marshal.StringToCoTaskMemUTF8("DirectorPrompt");
                secretKey        = Marshal.StringToCoTaskMemUTF8("secret-id");
                secretValue      = Marshal.StringToCoTaskMemUTF8(key);

                GHashTableInsert(Handle, applicationKey, applicationValue);
                GHashTableInsert(Handle, secretKey, secretValue);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (Handle != 0)
                GHashTableDestroy(Handle);

            Marshal.FreeCoTaskMem(applicationKey);
            Marshal.FreeCoTaskMem(applicationValue);
            Marshal.FreeCoTaskMem(secretKey);
            Marshal.FreeCoTaskMem(secretValue);
        }
    }
}
