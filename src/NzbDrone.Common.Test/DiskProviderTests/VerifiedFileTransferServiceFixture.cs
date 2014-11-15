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

namespace NzbDrone.Common.Test.DiskProviderTests
{
    [TestFixture]
    public class VerifiedFileTransferServiceFixture : TestBase<VerifiedFileTransferService>
    {
        private readonly String _sourcePath = @"C:\source\my.video.mkv".AsOsAgnostic();
        private readonly String _targetPath = @"C:\target\my.video.mkv".AsOsAgnostic();
        private readonly String _backupPath = @"C:\source\my.video.mkv.movebackup".AsOsAgnostic();

        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(v => v.TransferFile(It.IsAny<String>(), It.IsAny<String>(), It.IsAny<TransferMode>(), false))
                .Returns<String, String, TransferMode, Boolean>((s,t,m,o) => m);

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
                .Setup(v => v.TransferFile(_sourcePath, _targetPath, TransferMode.Copy, false))
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
                .Setup(v => v.TransferFile(_sourcePath, _targetPath, TransferMode.Copy, false))
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
                .Setup(v => v.TransferFile(_backupPath, _targetPath, TransferMode.Move, false))
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
                .Verify(v => v.TransferFile(_sourcePath, _targetPath, TransferMode.Copy, false), Times.Once());

            VerifyDeletedFile(_sourcePath);
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

        private void VerifyDeletedFile(String filePath)
        {
            var path = filePath;

            Mocker.GetMock<IDiskProvider>()
                .Verify(v => v.DeleteFile(path), Times.Once());
        }
    }
}
