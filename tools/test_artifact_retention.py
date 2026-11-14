"""Small filesystem fixtures exercise retention without opening any release ZIP."""
import contextlib,io,json,os,sys,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
import maintain_artifacts as retention
class RetentionChecks(unittest.TestCase):
    def setUp(self):
        parent=retention.ROOT/'ServoAnimator/DevelopmentArtifacts/Checks'
        parent.mkdir(parents=True,exist_ok=True)
        self.temp=tempfile.TemporaryDirectory(prefix='retention-',dir=parent)
        self.root=Path(self.temp.name);self.previous=retention.ROOT;retention.ROOT=self.root
        self.write('tools/retention-policy.json',json.dumps({'releaseVersionsToKeep':2,'publishedVersionsToKeep':2,'stagingMaxAgeDays':7}))
    def tearDown(self):
        retention.ROOT=self.previous;self.temp.cleanup()
    def write(self,path,text='keep'):
        p=self.root/path;p.parent.mkdir(parents=True,exist_ok=True);p.write_text(text);return p
    def apply(self):
        with patch.object(sys,'argv',['maintain_artifacts.py','--apply']),contextlib.redirect_stdout(io.StringIO()):retention.main()
    def test_releases_are_archived_with_config_and_current_is_untouched(self):
        for v in ['1.0.0','2.0.0','3.0.0']:self.write('Releases/Desktop/'+v+'/J5_Animator/Config/project.json',v)
        current=self.write('Releases/Desktop/Current/J5_Animator/Config/project.json','current')
        shared=self.write('Config/Projects/project.json','shared')
        self.apply()
        self.assertEqual((self.root/'Archive/RetiredReleases/Desktop/1.0.0/J5_Animator/Config/project.json').read_text(),'1.0.0')
        self.assertTrue((self.root/'Releases/Desktop/2.0.0').is_dir());self.assertTrue((self.root/'Releases/Desktop/3.0.0').is_dir())
        self.assertEqual(current.read_text(),'current');self.assertEqual(shared.read_text(),'shared')
    def test_generated_retention_protects_unmarked_and_settings(self):
        for v in ['0.5.0','1.0.0','2.0.0','3.0.0']:
            p=self.write('Johnny5VR/packaging/publish-'+v+'/Johnny5VR.exe').parent;retention.mark(p,'publish',v)
        settings=self.write('Johnny5VR/packaging/publish-1.0.0/Settings/viewer.json')
        unmarked=self.write('Johnny5VR/packaging/publish-0.1.0/important.txt')
        preserved=self.write('Johnny5VR/packaging/editor-check/Config/project.json')
        old=self.write('Johnny5VR/packaging/staging/old/file.txt').parent;retention.mark(old,'stage');os.utime(old/retention.MARKER,(0,0))
        fresh=self.write('Johnny5VR/packaging/staging/fresh/file.txt').parent;retention.mark(fresh,'stage')
        self.apply()
        self.assertFalse((self.root/'Johnny5VR/packaging/publish-0.5.0').exists());self.assertFalse(old.exists())
        for p in [settings,unmarked,preserved,fresh]:self.assertTrue(p.exists())
        self.assertTrue((self.root/'Johnny5VR/packaging/publish-2.0.0').is_dir());self.assertTrue((self.root/'Johnny5VR/packaging/publish-3.0.0').is_dir())
    def test_cannot_mark_shared_config_as_disposable(self):
        p=self.write('Config/Projects/project.json').parents[1]
        with self.assertRaises(ValueError):retention.mark(p,'stage')
        self.assertFalse((p/retention.MARKER).exists())
if __name__=='__main__':unittest.main()
