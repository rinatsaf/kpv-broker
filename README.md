<img width="1280" height="751" alt="image" src="https://github.com/user-attachments/assets/38516e36-91a0-4a72-a8af-b04a684bcb81" />



## несогласованность в PublisherService:
PublishAsync валидирует queue через string.IsNullOrEmpty,
а PublishBatchAsync через string.IsNullOrWhiteSpace.
Из-за этого одиночная публикация принимает queue из пробелов, а batch — отклоняет.
Unit-тесты это поймали.



## Если очередь не должна состоять из пробелов
решение
 ``` string.IsNullOrEmpty(request.Message.Queue) ```
 замена на 
 ```string.IsNullOrWhiteSpace(request.Message.Queue)```
 <img width="1280" height="745" alt="image" src="https://github.com/user-attachments/assets/e5e10c81-cf01-4d61-9299-329b51eda690" />

 ## Если по требованиям очередь " " допустима, тогда кейсы с пробелами просто лишние
