"""Run updater regression tests in a temporary directory, then remove binaries."""
from pathlib import Path
import tempfile
import build_release as b

def main():
    with tempfile.TemporaryDirectory(prefix='comipa-updater-tests-') as folder:
        out=Path(folder)
        b.run([b.CSC,'/nologo','/target:winexe','/out:'+str(out/'ComiPaRT.exe'),b.DIST/'src/TestSleeper.cs'])
        b.run([b.CSC,'/nologo','/out:'+str(out/'UpdaterTests.exe'),*['/r:'+r for r in b.REFS],b.DIST/'src/Updater.cs',b.DIST/'src/UpdaterTests.cs',b.DIST/'src/AdditionalTests.cs'])
        b.run([out/'UpdaterTests.exe'])

if __name__=='__main__':main()
