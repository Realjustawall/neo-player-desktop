"""Make sqlite3 context managers close their file handles on exit.

Python's sqlite3.Connection context manager commits/rolls back but does not close.
On Windows that can leave the database file locked until GC. NEO uses short-lived,
connection-per-operation access, so closing at the end of each `with` is intended.
"""
import sqlite3

_original_connect = sqlite3.connect


class ClosingConnection(sqlite3.Connection):
    def __exit__(self, exc_type, exc, tb):
        try:
            return super().__exit__(exc_type, exc, tb)
        finally:
            self.close()


def _connect(*args, **kwargs):
    kwargs.setdefault('factory', ClosingConnection)
    return _original_connect(*args, **kwargs)


sqlite3.connect = _connect
