; ===========================================================================
;  Read a name from the keyboard and greet
; ===========================================================================
;  Reads a line with DOS service 0Ah, then prints "Hello, <name>!".
;  While the program runs, click the Screen tab on the right and type.
; ===========================================================================

#make_COM#
org 100h

    mov dx, offset question
    mov ah, 09h
    int 21h

    ; --- Read a line (until Enter) ---------------------------------------
    mov dx, offset buffer   ; 1st byte of the buffer: maximum characters
    mov ah, 0Ah             ; DOS: buffered keyboard input
    int 21h                 ; The count of characters read goes to byte 2

    ; --- Put '$' after the text that was read ----------------------------
    mov bl, buffer[1]       ; BL <- number of characters read
    mov bh, 0               ; BX = BL (high byte 0 for a 16-bit index)
    mov buffer[bx+2], '$'   ; The text starts at the 3rd byte

    mov dx, offset hello
    mov ah, 09h
    int 21h

    mov dx, offset buffer + 2   ; The name that was read
    mov ah, 09h
    int 21h

    mov dl, '!'             ; Print a single character: DOS service 02h
    mov ah, 02h
    int 21h

    mov ax, 4C00h
    int 21h

question db 'Your name: $'
hello    db 13, 10, 'Hello, $'
buffer   db 30, ?, 31 dup(0) ; [maximum][read][characters...]
