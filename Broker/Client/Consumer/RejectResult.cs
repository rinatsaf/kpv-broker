namespace Client.Consumer;

public record struct RejectResult(bool Success, bool MovedToDlq);