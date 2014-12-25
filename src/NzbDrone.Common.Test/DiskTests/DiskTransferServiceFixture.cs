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

        /*
        [Test]
        public void should_be_able_to_hardlink_file()
        {
            var sourceDir = GetTempFilePath();
            var source = Path.Combine(sourceDir, "test.txt");
            var destination = Path.Combine(sourceDir, "destination.txt");

            Directory.CreateDirectory(sourceDir);

            Subject.WriteAllText(source, "SourceFile");

            var result = Subject.TransferFile(source, destination, TransferMode.HardLink);

            result.Should().Be(TransferMode.HardLink);

            File.AppendAllText(source, "Test");
            File.ReadAllText(destination).Should().Be("SourceFileTest");
        }

        private void DoHardLinkRename(FileShare fileShare)
        {
            var sourceDir = GetTempFilePath();
            var source = Path.Combine(sourceDir, "test.txt");
            var destination = Path.Combine(sourceDir, "destination.txt");
            var rename = Path.Combine(sourceDir, "rename.txt");

            Directory.CreateDirectory(sourceDir);

            Subject.WriteAllText(source, "SourceFile");

            Subject.TransferFile(source, destination, TransferMode.HardLink);

            using (var stream = new FileStream(source, FileMode.Open, FileAccess.Read, fileShare))
            {
                stream.ReadByte();

                Subject.MoveSingleFile(destination, rename);

                stream.ReadByte();
            }

            File.Exists(rename).Should().BeTrue();
            File.Exists(destination).Should().BeFalse();

            File.AppendAllText(source, "Test");
            File.ReadAllText(rename).Should().Be("SourceFileTest");
        }

        [Test]
        public void should_be_able_to_rename_open_hardlinks_with_fileshare_delete()
        {
            DoHardLinkRename(FileShare.Delete);
        }

        [Test]
        public void should_not_be_able_to_rename_open_hardlinks_with_fileshare_none()
        {
            Assert.Throws<IOException>(() => DoHardLinkRename(FileShare.None));
        }

        [Test]
        public void should_not_be_able_to_rename_open_hardlinks_with_fileshare_write()
        {
            Assert.Throws<IOException>(() => DoHardLinkRename(FileShare.Read));
        }
        */

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

        private void VerifyDeletedFile(String filePath)
        {
            var path = filePath;

            Mocker.GetMock<IDiskProvider>()
                .Verify(v => v.DeleteFile(path), Times.Once());
        }
    }
}
