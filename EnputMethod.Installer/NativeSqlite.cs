using System.Runtime.InteropServices;

namespace EnputMethod.Installer;

internal sealed class NativeSqliteConnection : IDisposable
{
    private const int Ok = 0;
    private const int ReadWrite = 0x00000002;
    private const int Create = 0x00000004;
    private const int FullMutex = 0x00010000;
    private IntPtr _database;

    internal NativeSqliteConnection(string path)
    {
        int result = sqlite3_open_v2(path, out _database, ReadWrite | Create | FullMutex, IntPtr.Zero);
        if (result != Ok) throw new InvalidOperationException($"SQLite open failed ({result}): {ErrorMessage}");
        Execute("PRAGMA foreign_keys = ON; PRAGMA journal_mode = DELETE; PRAGMA synchronous = NORMAL;");
    }

    internal string ErrorMessage => _database == IntPtr.Zero ? "database handle unavailable" : Marshal.PtrToStringUTF8(sqlite3_errmsg(_database)) ?? "unknown SQLite error";
    internal IntPtr Handle => _database;

    internal void Execute(string sql)
    {
        int result = sqlite3_exec(_database, sql, IntPtr.Zero, IntPtr.Zero, out IntPtr error);
        if (result == Ok) return;
        string message = error == IntPtr.Zero ? ErrorMessage : Marshal.PtrToStringUTF8(error) ?? ErrorMessage;
        if (error != IntPtr.Zero) sqlite3_free(error);
        throw new InvalidOperationException($"SQLite execution failed ({result}): {message}");
    }

    internal NativeSqliteStatement Prepare(string sql) => new(this, sql);

    internal int ScalarInt(string sql)
    {
        using NativeSqliteStatement statement = Prepare(sql);
        int result = Api.sqlite3_step(statement.Handle);
        if (result != Api.Row) throw new InvalidOperationException($"SQLite query failed ({result}): {ErrorMessage}");
        return Api.sqlite3_column_int(statement.Handle, 0);
    }

    public void Dispose()
    {
        if (_database == IntPtr.Zero) return;
        _ = sqlite3_close_v2(_database);
        _database = IntPtr.Zero;
    }

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Winapi)]
    private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr database, int flags, IntPtr vfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Winapi)]
    private static extern int sqlite3_close_v2(IntPtr database);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Winapi)]
    private static extern IntPtr sqlite3_errmsg(IntPtr database);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Winapi)]
    private static extern int sqlite3_exec(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr argument, out IntPtr error);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Winapi)]
    private static extern void sqlite3_free(IntPtr value);

    internal static class Api
    {
        internal const int Done = 101;
        internal const int Row = 100;
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Winapi)] internal static extern int sqlite3_prepare_v2(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int length, out IntPtr statement, IntPtr tail);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Winapi)] internal static extern int sqlite3_bind_text(IntPtr statement, int index, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int length, IntPtr destructor);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Winapi)] internal static extern int sqlite3_bind_int(IntPtr statement, int index, int value);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Winapi)] internal static extern int sqlite3_step(IntPtr statement);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Winapi)] internal static extern int sqlite3_reset(IntPtr statement);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Winapi)] internal static extern int sqlite3_clear_bindings(IntPtr statement);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Winapi)] internal static extern int sqlite3_finalize(IntPtr statement);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Winapi)] internal static extern int sqlite3_column_int(IntPtr statement, int column);
    }
}

internal sealed class NativeSqliteStatement : IDisposable
{
    private static readonly IntPtr Transient = new(-1);
    private readonly NativeSqliteConnection _connection;
    private IntPtr _statement;

    internal NativeSqliteStatement(NativeSqliteConnection connection, string sql)
    {
        _connection = connection;
        int result = NativeSqliteConnection.Api.sqlite3_prepare_v2(connection.Handle, sql, -1, out _statement, IntPtr.Zero);
        if (result != 0) throw new InvalidOperationException($"SQLite prepare failed ({result}): {connection.ErrorMessage}");
    }

    internal void BindText(int index, string value)
    {
        int result = NativeSqliteConnection.Api.sqlite3_bind_text(_statement, index, value, -1, Transient);
        if (result != 0) throw new InvalidOperationException($"SQLite bind failed ({result}): {_connection.ErrorMessage}");
    }

    internal void BindInt(int index, int value)
    {
        int result = NativeSqliteConnection.Api.sqlite3_bind_int(_statement, index, value);
        if (result != 0) throw new InvalidOperationException($"SQLite bind failed ({result}): {_connection.ErrorMessage}");
    }

    internal void Execute()
    {
        int result = NativeSqliteConnection.Api.sqlite3_step(_statement);
        if (result != NativeSqliteConnection.Api.Done) throw new InvalidOperationException($"SQLite step failed ({result}): {_connection.ErrorMessage}");
        _ = NativeSqliteConnection.Api.sqlite3_reset(_statement);
        _ = NativeSqliteConnection.Api.sqlite3_clear_bindings(_statement);
    }

    internal IntPtr Handle => _statement;

    public void Dispose()
    {
        if (_statement == IntPtr.Zero) return;
        _ = NativeSqliteConnection.Api.sqlite3_finalize(_statement);
        _statement = IntPtr.Zero;
    }
}
