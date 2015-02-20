using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Test.Common;
using FluentAssertions;

namespace NzbDrone.Common.Test.DiskTests
{
    [TestFixture]
    public class DiskTransferServiceFixture : TestBase<DiskTransferService>
    {
        private readonly String _sourcePath = @"C:\source\my.video.mkv".AsOsAgnostic();
        private readonly String _targetPath = @"C:\target\my.video.mkv".AsOsAgnostic();
        private readonly String _backupPath = @"C:\source\my.video.mkv.movebackup".AsOsAgnostic();

        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(v => v.GetFileSize(_sourcePath))
                .Returns(1000);
        }

        [Test]
        public void should_hardlink_only()
        {
            WithSuccessfulHardlink();

            var result = Subject.TransferFileVerified(_sourcePath, _targetPath, TransferMode.HardLink);

            result.Should().Be(TransferMode.HardLink);
        }

        [Test]
        public void should_throw_if_hardlink_only_failed()
        {
            Assert.Throws<IOException>(() => Subject.TransferFileVerified(_sourcePath, _targetPath, TransferMode.HardLink));
        }

        [Test]
        public void should_retry_if_partial_copy()
        {
            WithIncompleteTransfer();

            var retry = 0;
            Mocker.GetMock<IDiskProvider>()
                .Setup(v => v.CopySingleFile(_sourcePath, _targetPath, false))
                .Callback(() =>
                    {
                        if (retry++ == 1) WithCompletedTransfer();
                    });

            var result = Subject.TransferFileVerified(_sourcePath, _targetPath, TransferMode.Copy);

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_retry_twice_if_partial_copy()
        {
            WithIncompleteTransfer();

            var retry = 0;
            Mocker.GetMock<IDiskProvider>()
                .Setup(v => v.CopySingleFile(_sourcePath, _targetPath, false))
                .Callback(() =>
                    {
                        if (retry++ == 3) throw new Exception("Test Failed, retried too many times.");
                    });

            Assert.Throws<IOException>(() => Subject.TransferFileVerified(_sourcePath, _targetPath, TransferMode.Copy));

            ExceptionVerification.ExpectedWarns(1);
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_hardlink_before_move()
        {
            WithSuccessfulHardlink();
            WithCompletedTransfer();

            var result = Subject.TransferFileVerified(_sourcePath, _targetPath, TransferMode.Move);

            Mocker.GetMock<IDiskProvider>()
                .Verify(v => v.TryCreateHardLink(_sourcePath, _backupPath), Times.Once());
        }

        [Test]
        public void should_remove_source_after_move()
        {
            WithSuccessfulHardlink();
            WithCompletedTransfer();

            var result = Subject.TransferFileVerified(_sourcePath, _targetPath, TransferMode.Move);

            VerifyDeletedFile(_sourcePath);
        }

        [Test]
        public void should_remove_backup_if_move_throws()
        {
            WithSuccessfulHardlink();

            Mocker.GetMock<IDiskProvider>()
                .Setup(v => v.TryCreateHardLink(It.IsAny<String>(), It.IsAny<String>()))
                .Callback(() =>
                    {
                        Mocker.GetMock<IDiskProvider>()
                            .Setup(v => v.FileExists(_backupPath))
                            .Returns(true);
                    });

            Mocker.GetMock<IDiskProvider>()
                .Setup(v => v.MoveSingleFile(_backupPath, _targetPath, false))
                .Throws(new IOException("Blackbox IO error"));

            Assert.Throws<IOException>(() => Subject.TransferFileVerified(_sourcePath, _targetPath, TransferMode.Move));

            VerifyDeletedFile(_backupPath);

            ExceptionVerification.ExpectedWarns(1);
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_fallback_to_copy_if_hardlink_failed()
        {
            WithCompletedTransfer();

            var result = Subject.TransferFileVerified(_sourcePath, _targetPath, TransferMode.Move);

            Mocker.GetMock<IDiskProvider>()
                .Verify(v => v.CopySingleFile(_sourcePath, _targetPath, false), Times.Once());

            VerifyDeletedFile(_sourcePath);
        }

        [Test]
        public void CopyFolder_should_copy_folder()
        {
            WithRealDiskProvider();

            var source = GetFilledTempFolder();
            var destination = new DirectoryInfo(GetTempFilePath());

            Subject.TransferFolder(source.FullName, destination.FullName, TransferMode.Copy);

            VerifyCopyFolder(source.FullName, destination.FullName);
        }

        [Test]
        public void CopyFolder_should_overwrite_existing_folder()
        {
            WithRealDiskProvider();

            var source = GetFilledTempFolder();
            var destination = new DirectoryInfo(GetTempFilePath());
            Subject.TransferFolder(source.FullName, destination.FullName, TransferMode.Copy);

            //Delete Random File
            destination.GetFiles("*.*", SearchOption.AllDirectories).First().Delete();

            Subject.TransferFolder(source.FullName, destination.FullName, TransferMode.Copy);

            VerifyCopyFolder(source.FullName, destination.FullName);
        }


        [Test]
        public void MoveFolder_should_move_folder()
        {
            WithRealDiskProvider();

            var original = GetFilledTempFolder();
            var source = new DirectoryInfo(GetTempFilePath());
            var destination = new DirectoryInfo(GetTempFilePath());

            Subject.TransferFolder(original.FullName, source.FullName, TransferMode.Copy);

            Subject.TransferFolder(source.FullName, destination.FullName, TransferMode.Move);

            VerifyMoveFolder(original.FullName, source.FullName, destination.FullName);
        }

        [Test]
        public void MoveFolder_should_overwrite_existing_folder()
        {
            WithRealDiskProvider();

            var original = GetFilledTempFolder();
            var source = new DirectoryInfo(GetTempFilePath());
            var destination = new DirectoryInfo(GetTempFilePath());

            Subject.TransferFolder(original.FullName, source.FullName, TransferMode.Copy);
            Subject.TransferFolder(original.FullName, destination.FullName, TransferMode.Copy);

            Subject.TransferFolder(source.FullName, destination.FullName, TransferMode.Move);

            VerifyMoveFolder(original.FullName, source.FullName, destination.FullName);
        }

        public DirectoryInfo GetFilledTempFolder()
        {
            var tempFolder = GetTempFilePath();
            Directory.CreateDirectory(tempFolder);

            File.WriteAllText(Path.Combine(tempFolder, Path.GetRandomFileName()), "RootFile");

            var subDir = Path.Combine(tempFolder, Path.GetRandomFileName());
            Directory.CreateDirectory(subDir);

            File.WriteAllText(Path.Combine(subDir, Path.GetRandomFileName()), "SubFile1");
            File.WriteAllText(Path.Combine(subDir, Path.GetRandomFileName()), "SubFile2");

            return new DirectoryInfo(tempFolder);
        }

        private void WithSuccessfulHardlink()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(v => v.TryCreateHardLink(It.IsAny<String>(), It.IsAny<String>()))
                .Returns(true);
        }

        private void WithCompletedTransfer()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(v => v.GetFileSize(_targetPath))
                .Returns(1000);
        }

        private void WithIncompleteTransfer()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(v => v.GetFileSize(_targetPath))
                .Returns(900);
        }

        private void WithRealDiskProvider()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(v => v.FolderExists(It.IsAny<string>()))
                .Returns<string>(v => Directory.Exists(v));

            Mocker.GetMock<IDiskProvider>()
                .Setup(v => v.FileExists(It.IsAny<string>()))
                .Returns<string>(v => File.Exists(v));

            Mocker.GetMock<IDiskProvider>()
                .Setup(v => v.CreateFolder(It.IsAny<string>()))
                .Callback<string>(v => Directory.CreateDirectory(v));

            Mocker.GetMock<IDiskProvider>()
                .Setup(v => v.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()))
                .Callback<string, bool>((v,r) => Directory.Delete(v, r));

            Mocker.GetMock<IDiskProvider>()
                .Setup(v => v.DeleteFile(It.IsAny<string>()))
                .Callback<string>(v => File.Delete(v));

            Mocker.GetMock<IDiskProvider>()
                .Setup(v => v.GetDirectoryInfos(It.IsAny<string>()))
                .Returns<string>(v => new DirectoryInfo(v).GetDirectories().ToList());

            Mocker.GetMock<IDiskProvider>()
                .Setup(v => v.GetFileInfos(It.IsAny<string>()))
                .Returns<string>(v => new DirectoryInfo(v).GetFiles().ToList());

            Mocker.GetMock<IDiskProvider>()
                .Setup(v => v.CopySingleFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Callback<string, string, bool>((s, d, o) => File.Copy(s, d, o));

            Mocker.GetMock<IDiskProvider>()
                .Setup(v => v.MoveSingleFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Callback<string, string, bool>((s,d,o) => {
                    if (File.Exists(d) && o) File.Delete(d);
                    File.Move(s, d);
                });
            
        }

        private void VerifyCopyFolder(string source, string destination)
        {
            var sourceFiles = Directory.GetFileSystemEntries(source, "*", SearchOption.AllDirectories).Select(v => v.Substring(source.Length + 1)).ToArray();
            var destFiles = Directory.GetFileSystemEntries(destination, "*", SearchOption.AllDirectories).Select(v => v.Substring(destination.Length + 1)).ToArray();

            CollectionAssert.AreEquivalent(sourceFiles, destFiles);
        }

        private void VerifyMoveFolder(string source, string from, string destination)
        {
            Directory.Exists(from).Should().BeFalse();

            var sourceFiles = Directory.GetFileSystemEntries(source, "*", SearchOption.AllDirectories).Select(v => v.Substring(source.Length + 1)).ToArray();
            var destFiles = Directory.GetFileSystemEntries(destination, "*", SearchOption.AllDirectories).Select(v => v.Substring(destination.Length + 1)).ToArray();

            CollectionAssert.AreEquivalent(sourceFiles, destFiles);
        }

        private void VerifyDeletedFile(String filePath)
        {
            var path = filePath;

            Mocker.GetMock<IDiskProvider>()
                .Verify(v => v.DeleteFile(path), Times.Once());
        }
    }
}
