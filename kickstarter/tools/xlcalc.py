"""pycel set-up with the two patches it needs on Python 3.14 / numpy 2. Import `compiler(path)` from here."""
import ast

if not hasattr(ast, "Str"):  # pycel predates Python 3.12's removal of ast.Str
    ast.Str = lambda s: ast.Constant(value=s)  # noqa: E731

from pycel import ExcelCompiler, excellib  # noqa: E402

_orig_round = excellib._round


def _round(number, num_digits, rounding):
    # numpy 2 floats repr as 'np.float64(x)', which pycel's Decimal(repr()) cannot parse
    return _orig_round(number if isinstance(number, str) else float(number), num_digits, rounding=rounding)


excellib._round = _round


def compiler(path):
    return ExcelCompiler(filename=path)
