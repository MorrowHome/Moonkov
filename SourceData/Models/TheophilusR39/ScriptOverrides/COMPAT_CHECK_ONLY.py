"""Read-only compatibility preflight; writes JSON reports only."""
import sys
from pathlib import Path
ROOT=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT))
from compat_readonly import run
run(ROOT)
