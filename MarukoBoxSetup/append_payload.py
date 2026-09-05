#!/usr/bin/env python3
"""
把 payload.zip 追加到安装器 exe 末尾，生成自解压安装包。

文件尾部布局：
    [ ... exe 本体 ... ][ zip 数据 ][ 8 字节 zip 长度(Int64 LE) ][ magic "MARUKOPAYLOAD01" ]

PE 加载器会忽略文件尾部的附加数据，因此 exe 依然可正常执行；
安装器运行时按 magic + 长度反向定位 payload 并解压（见 Program.cs）。

用法：
    python append_payload.py [stub.exe] [payload.zip] [output.exe]
"""
import os
import struct
import sys

MAGIC = b"MARUKOPAYLOAD01"

DEFAULT_STUB = r"C:\mb_setup\MarukoBoxSetup.exe"
DEFAULT_ZIP = os.path.join(os.environ.get("TEMP", "."), "mb_payload.zip")
DEFAULT_OUT = r"C:\mb_setup\MarukoBoxSetup_1.0.0.exe"


def main() -> int:
    stub_path = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_STUB
    zip_path = sys.argv[2] if len(sys.argv) > 2 else DEFAULT_ZIP
    out_path = sys.argv[3] if len(sys.argv) > 3 else DEFAULT_OUT

    for label, path in (("stub", stub_path), ("payload", zip_path)):
        if not os.path.isfile(path):
            print(f"[错误] 找不到 {label} 文件：{path}")
            return 1

    with open(stub_path, "rb") as f:
        stub = f.read()
    with open(zip_path, "rb") as f:
        payload = f.read()

    with open(out_path, "wb") as f:
        f.write(stub)
        f.write(payload)
        f.write(struct.pack("<q", len(payload)))
        f.write(MAGIC)

    mb = 1024 * 1024
    print(f"[OK] 已生成：{out_path}")
    print(f"     stub    : {len(stub) / mb:8.1f} MB")
    print(f"     payload : {len(payload) / mb:8.1f} MB")
    print(f"     合计    : {os.path.getsize(out_path) / mb:8.1f} MB")
    return 0


if __name__ == "__main__":
    sys.exit(main())
