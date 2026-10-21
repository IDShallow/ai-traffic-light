using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeTrafficLight
{
    /// <summary>
    /// 最小只读 SQLite 访问，使用 Windows 自带的 winsqlite3.dll。
    /// 只做「打开 → prepare → 逐行读 → 关闭」，够读 Codex 桌面端的会话库即可。
    /// </summary>
    internal sealed class SqliteDb : IDisposable
    {
        private const int FlagReadOnly = 1;
        private const int Row = 100;

        private IntPtr _db;

        private SqliteDb(IntPtr db)
        {
            _db = db;
        }

        public static SqliteDb OpenReadOnly(string path)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;
            IntPtr db = IntPtr.Zero;
            try
            {
                int rc = Native.sqlite3_open_v2(path, out db, FlagReadOnly, IntPtr.Zero);
                if (rc != 0)
                {
                    if (db != IntPtr.Zero) Native.sqlite3_close(db);
                    return null;
                }
                Native.sqlite3_busy_timeout(db, 500);
                return new SqliteDb(db);
            }
            catch
            {
                // 老系统没有 winsqlite3.dll：当作不可用，走文件回退
                if (db != IntPtr.Zero) { try { Native.sqlite3_close(db); } catch { } }
                return null;
            }
        }

        public List<object[]> Query(string sql)
        {
            List<object[]> rows = new List<object[]>();
            if (_db == IntPtr.Zero || string.IsNullOrEmpty(sql)) return rows;

            IntPtr stmt = IntPtr.Zero;
            try
            {
                if (Native.sqlite3_prepare_v2(_db, sql, -1, out stmt, IntPtr.Zero) != 0) return rows;
                while (Native.sqlite3_step(stmt) == Row)
                {
                    int n = Native.sqlite3_column_count(stmt);
                    object[] row = new object[n];
                    for (int i = 0; i < n; i++) row[i] = Cell(stmt, i);
                    rows.Add(row);
                }
            }
            catch { }
            finally
            {
                if (stmt != IntPtr.Zero) { try { Native.sqlite3_finalize(stmt); } catch { } }
            }
            return rows;
        }

        private static object Cell(IntPtr stmt, int i)
        {
            int type = Native.sqlite3_column_type(stmt, i);
            if (type == 1) return Native.sqlite3_column_int64(stmt, i);
            if (type == 2) return Native.sqlite3_column_double(stmt, i);
            if (type == 5) return null;

            IntPtr p = Native.sqlite3_column_text(stmt, i);
            int n = Native.sqlite3_column_bytes(stmt, i);
            if (p == IntPtr.Zero || n <= 0) return "";
            byte[] buf = new byte[n];
            Marshal.Copy(p, buf, 0, n);
            return Encoding.UTF8.GetString(buf);
        }

        public static string Str(object[] row, int i)
        {
            return row != null && i >= 0 && i < row.Length && row[i] is string ? (string)row[i] : "";
        }

        public static long Long(object[] row, int i)
        {
            return row != null && i >= 0 && i < row.Length && row[i] is long ? (long)row[i] : 0L;
        }

        public void Dispose()
        {
            if (_db == IntPtr.Zero) return;
            try { Native.sqlite3_close(_db); } catch { }
            _db = IntPtr.Zero;
        }

        private static class Native
        {
            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_open_v2(
                [MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr db, int flags, IntPtr vfs);

            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_close(IntPtr db);

            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_busy_timeout(IntPtr db, int ms);

            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_prepare_v2(
                IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int nByte, out IntPtr stmt, IntPtr tail);

            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_step(IntPtr stmt);

            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_finalize(IntPtr stmt);

            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_column_count(IntPtr stmt);

            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_column_type(IntPtr stmt, int i);

            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            public static extern long sqlite3_column_int64(IntPtr stmt, int i);

            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            public static extern double sqlite3_column_double(IntPtr stmt, int i);

            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            public static extern IntPtr sqlite3_column_text(IntPtr stmt, int i);

            [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
            public static extern int sqlite3_column_bytes(IntPtr stmt, int i);
        }
    }
}
