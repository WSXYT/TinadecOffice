use async_trait::async_trait;
use codex_utils_absolute_path::AbsolutePathBuf;
use once_cell::sync::Lazy;
use std::fs;
use std::io;
use std::path::Path;

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct CreateDirectoryOptions {
    pub recursive: bool,
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct RemoveOptions {
    pub recursive: bool,
    pub force: bool,
}

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub struct CopyOptions {
    pub recursive: bool,
}

#[derive(Clone, Debug, Eq, PartialEq)]
pub struct FileMetadata {
    pub is_directory: bool,
    pub is_file: bool,
    pub is_symlink: bool,
    pub created_at_ms: i64,
    pub modified_at_ms: i64,
}

#[derive(Clone, Debug, Eq, PartialEq)]
pub struct ReadDirectoryEntry {
    pub file_name: String,
    pub is_directory: bool,
    pub is_file: bool,
}

#[derive(Clone, Debug, Eq, PartialEq)]
pub struct FileSystemSandboxContext;

pub type FileSystemResult<T> = io::Result<T>;

#[async_trait]
pub trait ExecutorFileSystem: Send + Sync {
    async fn read_file(
        &self,
        path: &AbsolutePathBuf,
        sandbox: Option<&FileSystemSandboxContext>,
    ) -> FileSystemResult<Vec<u8>>;

    async fn read_file_text(
        &self,
        path: &AbsolutePathBuf,
        sandbox: Option<&FileSystemSandboxContext>,
    ) -> FileSystemResult<String> {
        let bytes = self.read_file(path, sandbox).await?;
        String::from_utf8(bytes).map_err(|err| io::Error::new(io::ErrorKind::InvalidData, err))
    }

    async fn write_file(
        &self,
        path: &AbsolutePathBuf,
        contents: Vec<u8>,
        sandbox: Option<&FileSystemSandboxContext>,
    ) -> FileSystemResult<()>;

    async fn create_directory(
        &self,
        path: &AbsolutePathBuf,
        create_directory_options: CreateDirectoryOptions,
        sandbox: Option<&FileSystemSandboxContext>,
    ) -> FileSystemResult<()>;

    async fn get_metadata(
        &self,
        path: &AbsolutePathBuf,
        sandbox: Option<&FileSystemSandboxContext>,
    ) -> FileSystemResult<FileMetadata>;

    async fn read_directory(
        &self,
        path: &AbsolutePathBuf,
        sandbox: Option<&FileSystemSandboxContext>,
    ) -> FileSystemResult<Vec<ReadDirectoryEntry>>;

    async fn remove(
        &self,
        path: &AbsolutePathBuf,
        remove_options: RemoveOptions,
        sandbox: Option<&FileSystemSandboxContext>,
    ) -> FileSystemResult<()>;

    async fn copy(
        &self,
        source_path: &AbsolutePathBuf,
        destination_path: &AbsolutePathBuf,
        copy_options: CopyOptions,
        sandbox: Option<&FileSystemSandboxContext>,
    ) -> FileSystemResult<()>;
}

pub static LOCAL_FS: Lazy<Box<dyn ExecutorFileSystem>> = Lazy::new(|| Box::new(LocalFileSystem));

pub struct LocalFileSystem;

#[async_trait]
impl ExecutorFileSystem for LocalFileSystem {
    async fn read_file(
        &self,
        path: &AbsolutePathBuf,
        _sandbox: Option<&FileSystemSandboxContext>,
    ) -> FileSystemResult<Vec<u8>> {
        fs::read(path)
    }

    async fn write_file(
        &self,
        path: &AbsolutePathBuf,
        contents: Vec<u8>,
        _sandbox: Option<&FileSystemSandboxContext>,
    ) -> FileSystemResult<()> {
        fs::write(path, contents)
    }

    async fn create_directory(
        &self,
        path: &AbsolutePathBuf,
        create_directory_options: CreateDirectoryOptions,
        _sandbox: Option<&FileSystemSandboxContext>,
    ) -> FileSystemResult<()> {
        if create_directory_options.recursive {
            fs::create_dir_all(path)
        } else {
            fs::create_dir(path)
        }
    }

    async fn get_metadata(
        &self,
        path: &AbsolutePathBuf,
        _sandbox: Option<&FileSystemSandboxContext>,
    ) -> FileSystemResult<FileMetadata> {
        let metadata = fs::symlink_metadata(path)?;
        Ok(FileMetadata {
            is_directory: metadata.is_dir(),
            is_file: metadata.is_file(),
            is_symlink: metadata.file_type().is_symlink(),
            created_at_ms: system_time_ms(metadata.created().ok()),
            modified_at_ms: system_time_ms(metadata.modified().ok()),
        })
    }

    async fn read_directory(
        &self,
        path: &AbsolutePathBuf,
        _sandbox: Option<&FileSystemSandboxContext>,
    ) -> FileSystemResult<Vec<ReadDirectoryEntry>> {
        fs::read_dir(path)?
            .map(|entry| {
                let entry = entry?;
                let metadata = entry.metadata()?;
                Ok(ReadDirectoryEntry {
                    file_name: entry.file_name().to_string_lossy().to_string(),
                    is_directory: metadata.is_dir(),
                    is_file: metadata.is_file(),
                })
            })
            .collect()
    }

    async fn remove(
        &self,
        path: &AbsolutePathBuf,
        remove_options: RemoveOptions,
        _sandbox: Option<&FileSystemSandboxContext>,
    ) -> FileSystemResult<()> {
        if !Path::new(path.as_os_str()).exists() && remove_options.force {
            return Ok(());
        }

        if path.is_dir() {
            if remove_options.recursive {
                fs::remove_dir_all(path)
            } else {
                fs::remove_dir(path)
            }
        } else {
            fs::remove_file(path)
        }
    }

    async fn copy(
        &self,
        source_path: &AbsolutePathBuf,
        destination_path: &AbsolutePathBuf,
        copy_options: CopyOptions,
        _sandbox: Option<&FileSystemSandboxContext>,
    ) -> FileSystemResult<()> {
        if source_path.is_dir() {
            if !copy_options.recursive {
                return Err(io::Error::new(
                    io::ErrorKind::InvalidInput,
                    "recursive copy is required for directories",
                ));
            }
            copy_directory(source_path, destination_path)
        } else {
            fs::copy(source_path, destination_path).map(|_| ())
        }
    }
}

fn copy_directory(source: &Path, destination: &Path) -> io::Result<()> {
    fs::create_dir_all(destination)?;
    for entry in fs::read_dir(source)? {
        let entry = entry?;
        let target = destination.join(entry.file_name());
        if entry.file_type()?.is_dir() {
            copy_directory(&entry.path(), &target)?;
        } else {
            fs::copy(entry.path(), target)?;
        }
    }
    Ok(())
}

fn system_time_ms(value: Option<std::time::SystemTime>) -> i64 {
    value
        .and_then(|time| time.duration_since(std::time::UNIX_EPOCH).ok())
        .map(|duration| i64::try_from(duration.as_millis()).unwrap_or(i64::MAX))
        .unwrap_or(0)
}
