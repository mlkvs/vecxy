namespace Vecxy.Engine;

public class NotSupportedProjectTypeException(PROJECT_TYPE type) : Exception($"This type: {type} of project is not supported");